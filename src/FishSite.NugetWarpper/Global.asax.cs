using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Security;
using System.Web.SessionState;

namespace FishSite.NugetWarpper
{
	using System.IO;
	using System.Timers;
	using System.Web.Hosting;
	using Newtonsoft.Json;

	public class Global : System.Web.HttpApplication
	{

		protected void Application_Start(object sender, EventArgs e)
		{
			_timer = new Timer(1000 * 60 * 20) { AutoReset = false };
			_timer.Elapsed += (s, ee) =>
			{
				_timer.Stop();
				try
				{
					SaveAllCache();
				}
				catch (Exception ex)
				{
					File.WriteAllText(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "save.txt"), ex.ToString());
				}
				_timer.Start();
			};
			_timer.Start();
			File.WriteAllText(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "save.txt"), "started!");

		}

		Timer _timer;
		static readonly object _lockObject = new object();

		void SaveAllCache()
		{
			lock (_lockObject)
			{
				var targetType = typeof(MetaData);
				var enumerator = HttpRuntime.Cache.GetEnumerator();
				while (enumerator.MoveNext())
				{
					if (enumerator.Value != null && enumerator.Value.GetType() == targetType)
					{
						var obj = (MetaData)enumerator.Value;
						File.WriteAllText(obj.CachePath, JsonConvert.SerializeObject(obj));
					}
				}
				File.WriteAllText(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "save.txt"), $"saved! {DateTime.Now}");
			}
		}

		protected void Session_Start(object sender, EventArgs e)
		{

		}

		protected void Application_BeginRequest(object sender, EventArgs e)
		{

		}

		protected void Application_AuthenticateRequest(object sender, EventArgs e)
		{

		}

		protected void Application_Error(object sender, EventArgs e)
		{

		}

		protected void Session_End(object sender, EventArgs e)
		{

		}

		protected void Application_End(object sender, EventArgs e)
		{
			try
			{
				SaveAllCache();
			}
			catch (Exception ex)
			{
				File.WriteAllText(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "save.txt"), ex.ToString());
			}
		}
	}
}