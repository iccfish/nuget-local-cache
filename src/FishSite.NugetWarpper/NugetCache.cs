using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FishSite.NugetWarpper
{
	using System.IO;
	using System.Net;
	using System.Text;
	using System.Text.RegularExpressions;
	using System.Threading.Tasks;
	using System.Web.Caching;
	using System.Web.Hosting;
	using Newtonsoft.Json;

	public class NugetCache : IHttpModule
	{
		private bool _enableGzip;
		HttpRequest request;
		HttpApplication context;
		HttpResponse response;

		/// <summary>
		/// 初始化模块，并使其为处理请求做好准备。
		/// </summary>
		/// <param name="context">一个 <see cref="T:System.Web.HttpApplication"/>，它提供对 ASP.NET 应用程序内所有应用程序对象的公用的方法、属性和事件的访问</param>
		public void Init(HttpApplication context)
		{
			context.BeginRequest += Context_BeginRequest;
		}

		private void Context_BeginRequest(object sender, EventArgs e)
		{
			context = sender as HttpApplication;
			request = context.Request;
			response = context.Response;
			var uri = context.Request.Url;

			//是否可以用GZIP？
			_enableGzip = (request.Headers["Accept-Encoding"] ?? "").IndexOf("gzip") != -1;

			try
			{
				//要求没有查询并符合规则才处理
				if (!uri.Query.IsNullOrEmpty() || context.Request.HttpMethod != "GET" || !Regex.IsMatch(uri.LocalPath, @"^[a-z\d/\-_\.]+\.(json|nupkg)(.*?)$", RegexOptions.IgnoreCase))
				{
					DirectProxy(context);
				}
				else
				{
					CacheProxy(context);
				}
			}
			catch (Exception ex)
			{
				response.StatusCode = 502;
				response.StatusDescription = "Gateway Error";
				response.Write(ex.ToString());
			}

			context.Response.End();
		}


		void CacheProxy(HttpApplication app)
		{
			var request = app.Request;
			var response = app.Response;

			var localRoot = HostingEnvironment.ApplicationPhysicalPath;
			var localPath = request.Url.LocalPath.Replace('/', '\\').TrimStart('\\');
			var localCacheFile = Path.Combine(localRoot, localPath).Replace("-gz\\", "\\");
			var meta = localCacheFile + ".meta";
			var cacheKey = "nuget_" + localPath;
			var responseProcessed = false;

			//load from cache
			var cache = (MetaData)HttpRuntime.Cache[cacheKey];
			if (cache == null)
			{
				//缓存没有，尝试加载
				if (File.Exists(meta))
				{
					try
					{
						cache = JsonConvert.DeserializeObject<MetaData>(File.ReadAllText(meta));
						cache.CachePath = meta;
					}
					catch (Exception)
					{
						File.Delete(meta);
					}
				}
				//二次判断是否有，没有则初始化一个
				if (cache == null)
					cache = new MetaData()
					{
						CachePath = meta,
						NoCheckUpdate = localPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
					};
				HttpRuntime.Cache.Add(cacheKey, cache, null, Cache.NoAbsoluteExpiration, new TimeSpan(0, 30, 0), CacheItemPriority.Normal, (key, value, reason) =>
				{
					var m = value as MetaData;
					File.WriteAllText(m.CachePath, JsonConvert.SerializeObject(m));
				});
			}
			//是否需要更新缓存？
			if (cache.LastUpdate == null || (!cache.NoCheckUpdate && cache.LastUpdate.Value.Date < DateTime.Today) || !File.Exists(localCacheFile))
			{
				if (File.Exists(meta)) File.Delete(meta);
				if (File.Exists(localPath)) File.Delete(localPath);

				//刷新缓存
				var webrequest = (HttpWebRequest)WebRequest.Create(new Uri(new Uri("https://api.nuget.org/"), request.Url.PathAndQuery));
				webrequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

				foreach (var header in request.Headers.AllKeys.Where(s => s.StartsWith("x-", StringComparison.OrdinalIgnoreCase)))
				{
					request.Headers.Add(header, request.Headers[header]);
				}
				if (cache.LastModified != null)
					request.Headers["If-Modified-Since"] = cache.LastModified.Value.ToString("R");
				if (!cache.ETag.IsNullOrEmpty())
					request.Headers["If-None-Match"] = cache.ETag;

				HttpWebResponse webresponse;
				try
				{
					webresponse = (HttpWebResponse)webrequest.GetResponse();
				}
				catch (WebException ex) when (ex.Status == WebExceptionStatus.ProtocolError)
				{
					webresponse = (HttpWebResponse)ex.Response;

					response.StatusCode = 502;
					response.StatusDescription = webresponse.StatusDescription;

					response.Write($"Gateway Error. The upstream server returns {response.StatusCode}");

					webresponse.Close();
					return;
				}

				cache.LastUpdate = DateTime.Now;
				cache.HitCount++;

				if (webresponse.StatusCode == HttpStatusCode.NotModified)
				{
					cache.LastModified = webresponse.GetResponseHeader("Last-Modified").ToDateTimeNullable();
					//没有修改过
					response.Headers.Add("X-FishCache", "HIT | VERIFIED");
				}
				else if (webresponse.StatusCode == HttpStatusCode.OK)
				{
					response.Headers.Add("X-FishCache", "MISS");
					Directory.CreateDirectory(Path.GetDirectoryName(localCacheFile));

					cache.ResponseHeaders = new Dictionary<string, string>();
					foreach (var header in webresponse.Headers.AllKeys.Where(s => s.StartsWith("x-", StringComparison.OrdinalIgnoreCase)))
					{
						cache.ResponseHeaders[header] = webresponse.Headers[header];
					}
					cache.ETag = webresponse.GetResponseHeader("Etag");
					cache.LastModified = webresponse.GetResponseHeader("Last-Modified").ToDateTimeNullable();

					//copy
					try
					{
						if (request.Url.GetFileName().EndsWith(".json"))
						{
							var br = new StreamReader(webresponse.GetResponseStream(), Encoding.UTF8);
							var content = br.ReadToEnd();
							content = ReplaceHost(content);

							var buffer = Encoding.UTF8.GetBytes(content);

							var bufferGz = buffer.Compress();
							File.WriteAllBytes(localCacheFile, buffer);
							File.WriteAllBytes(localCacheFile + ".gz", bufferGz);
						}
						else
						{
							//临时文件
							var tmpfile = localCacheFile + "." + DateTime.Now.Ticks + ".tmp";
							var tempBuffer = new byte[0x1000];
							var srcStream = webresponse.GetResponseStream();

							//write to response at the same time
							foreach (var header in cache.ResponseHeaders)
							{
								response.AddHeader(header.Key, header.Value);
							}
							response.BufferOutput = false;
							if(webresponse.ContentLength>0)
								response.AddHeader("Content-Length", webresponse.ContentLength.ToString());
							response.Flush();

							using (var tmp = File.OpenWrite(tmpfile))
							{
								var tempReadCount = 0;
								while ((tempReadCount = srcStream.Read(tempBuffer, 0, tempBuffer.Length)) > 0)
								{
									if (!response.IsClientConnected)
									{
										break;
									}

									tmp.Write(tempBuffer, 0, tempReadCount);
									response.BinaryWrite(tempReadCount == tempBuffer.Length ? tempBuffer : tempBuffer.Take(tempReadCount).ToArray());
								}
							}
							if (!response.IsClientConnected)
							{
								File.Delete(tmpfile);
							}
							else
							{
								File.Move(tmpfile, localCacheFile);
							}
							responseProcessed = true;
						}
						File.WriteAllText(meta, JsonConvert.SerializeObject(cache));
					}
					catch (Exception)
					{

					}
				}

				webresponse.Close();
			}
			else
			{
				response.Headers.Add("X-FishCache", "HIT");
			}

			if (responseProcessed)
				return;

			foreach (var header in cache.ResponseHeaders)
			{
				response.Headers[header.Key] = header.Value;
			}

			//写入响应
			if (!_enableGzip || !request.Url.GetFileName().EndsWith(".json"))
			{
				response.TransmitFile(localCacheFile);
			}
			else
			{
				response.Headers["Content-Encoding"] = "gzip";
				response.TransmitFile(localCacheFile + ".gz");
			}
		}

		string ReplaceHost(string content)
		{
			return Regex.Replace(content, @"https://api\.nuget\.org/(?=packages|v3)", "http://" + request.Url.Authority + "/");
		}

		void DirectProxy(HttpApplication context)
		{
			var request = (HttpWebRequest)WebRequest.Create(new Uri(new Uri("https://api.nuget.org/"), context.Request.Url.PathAndQuery));
			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			foreach (var header in context.Request.Headers.AllKeys.Where(s => s.StartsWith("x-", StringComparison.OrdinalIgnoreCase)))
			{
				request.Headers.Add(header, context.Request.Headers[header]);
			}

			var response = (HttpWebResponse)request.GetResponse();
			//set headers
			foreach (var header in response.Headers.AllKeys.Where(s => s.StartsWith("x-", StringComparison.OrdinalIgnoreCase)))
			{
				context.Response.Headers.Add(header, response.Headers[header]);
			}
			context.Response.StatusCode = (int)response.StatusCode;

			//copy
			if (context.Request.Url.GetFileName().EndsWith(".json"))
			{
				var br = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
				var content = br.ReadToEnd();
				content = ReplaceHost(content);
				context.Response.BinaryWrite(Encoding.UTF8.GetBytes(content));
			}
			else
			{
				response.GetResponseStream().CopyTo(context.Response.OutputStream);
			}

			response.Close();
		}

		/// <summary>
		/// 处置由实现 <see cref="T:System.Web.IHttpModule"/> 的模块使用的资源（内存除外）。
		/// </summary>
		public void Dispose()
		{

		}
	}

	public class MetaData
	{
		[JsonIgnore]
		public string CachePath { get; set; }

		public DateTime? LastModified { get; set; }

		public string ETag { get; set; }

		public Dictionary<string, string> ResponseHeaders { get; set; }

		public DateTime? LastUpdate { get; set; }

		public int HitCount { get; set; }

		public bool NoCheckUpdate { get; set; }
	}
}