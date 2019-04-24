using System;
using System.Collections.Generic;
using System.IO;

namespace NNBot
{
	public class Config
	{
		
		public Config ()
		{
		}

		private static void readConfig(string file, Dictionary<String, String> dic)
		{
			Console.WriteLine("Reading config " + file);
			var data = File.ReadAllLines(file);
			foreach (string l in data)
			{
				var i = l.IndexOf("=");
				if (i >= 0)
				{
					var k = l.Substring(0, i).Trim();
					var v = l.Substring(i + 1).Trim();
					//dic.Add(k, v);
					dic[k] = v;
				}
			}
		}

		private static void setDefaults(Dictionary<String, String> dic)
		{
			dic["singleboost"] = "1000";
			dic["singletime"] = "10";
			dic["debugchat"] = "0";
		}

		public static Dictionary<String, String> LoadConfig(string file)
		{
			var dic = new Dictionary<String, String>();
			setDefaults(dic);
			readConfig("base.cfg", dic);
			readConfig(file, dic);
			return dic;
		}


	}
}
