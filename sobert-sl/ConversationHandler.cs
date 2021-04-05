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
		private double othertalk = 1, selftalk = 100, boost = 0;
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
					Thread.Sleep((int)(Convert.ToDouble(Bot.configuration["talkinterval"]) * 1000));
					tick();
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
				othertalk += message.Length;
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
			if (q < 500 && quiet > 500) q = 500;
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
			double talkProbNew = 0.001;
			string message;
			double td = Convert.ToDouble(Bot.configuration["talkdecay"]);
			double targetratio = Convert.ToDouble(Bot.configuration["targetratio"]);
			double talkadd = Convert.ToDouble(Bot.configuration["talkadd"]);
			double bonus = 0;
			lock (lck)
			{
				if (quiet <= 0) {
					credits += dblopt("credits_per_tick");
					if (talked_since_heard == 0) bonus += timeSourceChange * dblopt("bonus_per_singleminute");
				}
				bonus += dblopt("bonus_per_mention") * mentions;
				bonus -= dblopt("penalty_per_monocharacter") * talked_since_heard;
				credits *= dblopt("credits_decay");
				talkProbNew *= Math.Exp((credits + bonus) / dblopt("credits_div"));
				if (quiet > 0) quiet--;
				// old logic starts here
				double totalboost = 0;
				if (timeHeard < timeTalked || timeTalked > 5) totalboost += boost;
				if (thinking) talkProbNew = 0;
				message = "tHear=" + timeHeard.ToString("n2") + " tTalk=" + timeTalked.ToString("n2") + 
					" ts=" + timeSourceChange.ToString("n2") + " sc=" + slowcredits.ToString("n1") + " cr=" + credits.ToString("n1") + " bonus=" + bonus.ToString("n1") +
						" tsh=" + talked_since_heard.ToString() + 
				        " quiet=" + quiet.ToString() +
				        " prob=" + (talkProbNew*100).ToString("n2") + "%";
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
                        selftalk += s.Length;
						thinking = false;
						boost *= Convert.ToDouble(Bot.configuration["boostdecay"]);
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
