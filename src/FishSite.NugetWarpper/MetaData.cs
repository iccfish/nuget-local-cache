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