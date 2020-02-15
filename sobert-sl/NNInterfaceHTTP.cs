using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NNBot
{
    public class NNInterfaceHTTP
    {
		private static HttpClient client = new HttpClient();
		private static readonly object lck = new object();
        private static Dictionary<string, NNInterfaceHTTP> dict = new Dictionary<string, NNInterfaceHTTP>();
        static NNInterfaceHTTP()
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        }
        public static NNInterfaceHTTP getInterface(string key)
        {
            lock (lck)
            {
                NNInterfaceHTTP nni;
                bool got = dict.TryGetValue(key, out nni);
                if (got && nni != null) return nni;
                nni = new NNInterfaceHTTP(key);
                dict[key] = nni;
				nni.runThread();
                return nni;
            }
        }

		private string name;
		private delegate Task QueuedOperation();
		private BlockingCollection<QueuedOperation> queue = new BlockingCollection<QueuedOperation>();
        public NNInterfaceHTTP(string n)
        {
            name = n;
        }
		private void runThread()
		{
			Thread th = new Thread(() =>
			{
				Console.WriteLine("Thread for (" + name + ") running");
				while (true)
				{
					QueuedOperation op;
					if (!queue.TryTake(out op, 4 * 60 * 60 * 1000))
					{
						Console.WriteLine("Conversation timed out: " + name);
						deleteSelf();
						break;
					}
					if (queue.Count > 20)
						Console.WriteLine("Queue size: " + queue.Count);
					try
					{
                        Task.Run(async () => await op()).Wait();
					} catch (Exception e){
						Console.WriteLine("Exception in interface thread:" + e.ToString());
					}
				}
			});
			th.IsBackground = true;
			th.Start();
		}
		private void deleteSelf()
        {
            lock (lck)
            {
                if (dict[name] == this)
                {
                    Console.WriteLine("Removing conversation for (" + name + ")");
                    dict.Remove(name);
                }
            }
        }
		public void pushLine(string line)
        {
            if (line == "") return;
            queue.Add(async () =>
            {
				await pushLineNowAsync(line);
            });
        }

        public void getLine(Bot.Reply r)
        {
            queue.Add(async () =>
            {
				string rs = await getResponseNowAsync();
                r(rs.Trim());
            });
        }
		private async Task pushLineNowAsync(string line)
        {
            try
            {
				var jreq = new JObject();
				jreq["key"] = Bot.configuration["nnkey"] + ":" + name;
				jreq["text"] = line;
				var cnt = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(jreq.ToString()));
				HttpResponseMessage tsk = await client.PostAsync(Bot.configuration["nnurl"] + "put", cnt);
				if (!tsk.IsSuccessStatusCode)
					Console.WriteLine("resp: " + tsk);
            }
			catch (HttpRequestException e)
            {
				Console.WriteLine("Can't write to NN: " + e.ToString());
            }
        }

        private async Task<string> getResponseNowAsync()
        {
            try
            {
				var jreq = new JObject();
				jreq["key"] = Bot.configuration["nnkey"] + ":" + name;
				if (Bot.configuration.ContainsKey("badwords"))
					jreq["bad_words"] = new JArray(Bot.configuration["badwords"].Split(','));
				//Console.WriteLine("request: " + jreq);
				var cnt = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(jreq.ToString()));
				HttpResponseMessage tsk = await client.PostAsync(Bot.configuration["nnurl"] + "get", cnt);
				if (!tsk.IsSuccessStatusCode)
				{
					Console.WriteLine("resp: " + tsk);
					return "";
				}
				string resp_json = await tsk.Content.ReadAsStringAsync();
				var definition = new { text = "" };
				var resp_parsed = JsonConvert.DeserializeAnonymousType(resp_json, definition);
				return resp_parsed.text;
            }
			catch (HttpRequestException e)
            {
				Console.WriteLine("Can't read from NN: " + e.ToString());
				return "";
            }
        }
	}
}
