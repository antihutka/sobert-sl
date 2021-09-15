using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NNBot
{
	public class ConversationHandler
	{
		private readonly object lck = new object();
		private DateTime lastHeard, lastTalked, lastSourceChange;
		readonly string nnkey;
		private readonly Bot.Reply talk;
		private double credits = 0;
		private double slowcredits = 0;
		private int mentions = 0;
		private bool thinking = false;
		private int quiet = 0;
		string kw_last;
		string[] kw_split;
		StreamWriter logfile;
		private int titler_last_update = 0;
		private int talked_since_heard = 0;
		private string quiet_by;
		private string lastSource = "";

        public ConversationHandler(string key, Bot.Reply handler)
		{
			logfile = new StreamWriter("talkinfo." + Bot.configuration["firstname"] + "." + Bot.configuration["lastname"] + ".log");
			talk = handler;
			nnkey = key;
		}

		public void start()
		{
			lastTalked = DateTime.Now;
			lastHeard = lastTalked;
			lastSourceChange = DateTime.Now;
			Task.Run(() =>
			{
				while (true)
				{
					try {
						Thread.Sleep((int)(dblopt("talkinterval") * 1000));
						tick();
					} catch (Exception e)
					{
						System.Console.WriteLine("Exception in talk tick:", e);
						Thread.Sleep(15*1000);
					}
				}
			});
		}


		private static double dblopt(string s)
		{
			return Convert.ToDouble(Bot.configuration[s]);
		}

		private void transfer_credits(double a)
		{
			if (a>slowcredits) a=slowcredits;
			slowcredits -= a;
			credits += a;
		}

		public void incomingMessage(string message, bool fromObj, string source)
		{
			if (Convert.ToInt32(Bot.configuration["debugchat"]) > 0)
				Console.WriteLine("Incoming chat from " + source + ":" + message);
			NNInterfaceHTTP.getInterface(nnkey).pushLine(message);
			string kwc = Bot.configuration["keywords"];
			if (kw_last != kwc)
			{
				kw_split = kwc.Split(',');
				kw_last = kwc;
			}
			bool has_kw = kw_split.Any((s) => message.Contains(s));
			lock (lck)
			{
				if (lastSource != source) {
					lastSource = source;
					lastSourceChange = lastHeard;
				}
				lastHeard = DateTime.Now;
				talked_since_heard = 0;
				if (has_kw && !fromObj) {
					slowcredits += dblopt("credits_per_mention");
					mentions += 1;
				}
				if (quiet <= 0) {
					credits += dblopt("credits_per_character") * message.Length;
					transfer_credits(dblopt("transfer_per_character") * message.Length);
				}
			}
		}

		private void updateTitle()
		{
			int chan = Convert.ToInt32(Bot.configuration["titlerchannel"]);
			int q = quiet;
			if (chan != 0)
			{
				string message = "Told to be quiet by " + quiet_by + " for " + q;
				if (q == 0) message = ".";
				Bot.Client.Self.Chat(message, chan, OpenMetaverse.ChatType.Normal);
				titler_last_update = q;
			}
		}

		public void setquiet(int q, string user)
		{
			if (q > 9000) q = 9000;
			if (q < 0) q = 0;
			lock(lck)
			{
				quiet = q;
				quiet_by = user;
				updateTitle();
			}
		}

		private void tick()
		{
			DateTime now = DateTime.Now;
			double timeHeard = (now - lastHeard).TotalMinutes;
			double timeTalked = (now - lastTalked).TotalMinutes;
			double timeSourceChange = (now - lastSourceChange).TotalMinutes;
			double talkProbNew = 0.1/100;
			string message;
			double bonus = 0;
			lock (lck)
			{
				if (quiet <= 0) {
					credits += dblopt("credits_per_tick");
					if (talked_since_heard == 0) bonus += timeSourceChange * dblopt("bonus_per_singleminute");
				}
				transfer_credits(dblopt("transfer_per_tick"));
				bonus += dblopt("bonus_per_mention") * mentions;
				bonus -= dblopt("penalty_per_monocharacter") * talked_since_heard;
				credits *= dblopt("credits_decay");
				talkProbNew *= Math.Exp((credits + bonus) / dblopt("credits_div"));
				if (quiet > 0) quiet--;
				// old logic starts here
				if (thinking) talkProbNew = 0;
				message = "tH=" + timeHeard.ToString("n2") + " tT=" + timeTalked.ToString("n2") + 
					" ts=" + timeSourceChange.ToString("n2") + " sc=" + slowcredits.ToString("n0") + " cr=" + credits.ToString("n0") + " bonus=" + bonus.ToString("n0") +
						" tsh=" + talked_since_heard.ToString() + 
				        " q=" + quiet.ToString() +
				        " p=" + (talkProbNew*100).ToString("n2") + "%";
				if (Convert.ToInt32(Bot.configuration["talkinfo"]) > 0)
					                                     Console.WriteLine(message);
				Console.Title = message;
				if ((quiet == 0 && titler_last_update > 0) || Math.Abs(quiet - titler_last_update) >= 60)
					updateTitle();
			}
			logfile.WriteLine(message); logfile.Flush();

			if (!thinking && Bot.rand.NextDouble() < talkProbNew)
			{
				lock(lck) thinking = true;
				NNInterfaceHTTP.getInterface(nnkey).getLine((s) =>
				{
				    lock (lck)
				    {
						thinking = false;
						credits -= s.Length;
						talked_since_heard += s.Length;
						mentions = 0;
					}
					if (s != "")
						talk(s);
				});
				lastTalked = DateTime.Now;
			}
		}
	}
}
