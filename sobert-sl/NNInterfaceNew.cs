using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace NNBot
{
	public class NNInterfaceNew
	{
		private static readonly object lck = new object();
		private static Dictionary<string, NNInterfaceNew> dict = new Dictionary<string, NNInterfaceNew>();

		public static NNInterfaceNew getInterface(string key)
		{
			lock (lck)
			{
				NNInterfaceNew nni;
				bool got = dict.TryGetValue(key, out nni);
				if (got && nni != null) return nni;
				nni = new NNInterfaceNew(key);
				dict[key] = nni;
				nni.connect();
				return nni;
			}
		}

		private delegate void QueuedOperation();
		private string name;
		private BlockingCollection<QueuedOperation> queue = new BlockingCollection<QueuedOperation>();
		private NetworkStream connection;
		private StreamReader connectionR;

		public NNInterfaceNew (string n)
		{
			name = n;
		}

		public void pushLine(string line)
		{
			line = Regex.Replace(line, @"^\s*$[\r\n]*", "", RegexOptions.Multiline).TrimEnd();
			if (line == "") return;
			//Console.WriteLine("pushLine (" + name + ") " + line);
			queue.Add(() =>
			{
				pushLineNow(line);
			});
		}

		public void getLine(Bot.Reply r)
		{
			//Console.WriteLine("getLine (" + name + ")");
			queue.Add(() =>
			{
				r(getResponseNow().Trim());
			});
		}

		private void pushLineNow(string line)
		{
			if (connection == null) return;
			byte[] lineb = Encoding.UTF8.GetBytes(line + "\n");
			try
			{
				connection.Write(lineb, 0, lineb.Length);
			}
			catch (IOException e)
			{
				handleError("Can't write to NN: ", e);
			}
		}

		private string getResponseNow()
		{
			if (connection == null) return "";
			pushLineNow("");
			try
			{
				string msg = connectionR.ReadLine();
				return msg;
			}
			catch (IOException e)
			{
				handleError("Can't read from NN: ", e);
			}
			return "";
		}

		private void runThread()
		{
			Task.Run(() =>
			{
				Console.WriteLine("Thread for (" + name + ") running");
				while (true)
				{
					QueuedOperation op;
					if (!queue.TryTake(out op, 4 * 60 * 60 * 1000))
					{
						Console.WriteLine("Conversation timed out: " + name);
						connection.Close();
						connection = null;
						deleteSelf();
						break;
					}
					op();
				}
			});
		}

		private void connect()
		{
			try
			{
				var soc = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				soc.ReceiveTimeout = 300 * 1000;
				soc.Connect(Bot.configuration["nnhost"], Convert.ToInt32(Bot.configuration["nnport"]));
				connection = new NetworkStream(soc, true);
				connectionR = new StreamReader(connection, Encoding.UTF8);
				runThread();
			}
			catch (SocketException e)
			{
				handleError("Can't connect to NN: ", e);
			}
		}

		private void handleError(string errname, Exception e)
		{
			Console.WriteLine(errname + e.ToString());
			connection = null;
			deleteSelf();
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
	}
}

