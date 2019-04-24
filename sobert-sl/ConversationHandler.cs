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
		private bool thinking = false;
		private int quiet = 0;
		string kw_last;
		string[] kw_split;
		StreamWriter logfile;
		private int titler_last_update = 0;
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

		public void incomingMessage(string message, bool fromObj, string source)
		{
			if (Convert.ToInt32(Bot.configuration["debugchat"]) > 0)
				Console.WriteLine("Incoming chat from " + source + ":" + message);
			NNInterfaceNew.getInterface(nnkey).pushLine(message);
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
				if (has_kw && !fromObj) boost += Convert.ToDouble(Bot.configuration["boostamount"]);
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
			double talkProb = 0.01;
			string message;
			lock (lck)
			{
				double td = Convert.ToDouble(Bot.configuration["talkdecay"]);
				selftalk *= td;
				othertalk *= td;
				if (quiet > 0) quiet--;
				if (quiet > 900 && timeHeard > 900) quiet -= 14;
				double targetratio = Convert.ToDouble(Bot.configuration["targetratio"]);
				double talkadd = Convert.ToDouble(Bot.configuration["talkadd"]);
				double totalboost = boost;
				if (timeHeard < timeTalked) totalboost += Convert.ToDouble(Bot.configuration["respboost"]);
				if (timeHeard < timeTalked && timeSourceChange > Convert.ToDouble (Bot.configuration ["singletime"]))
					totalboost += Convert.ToDouble (Bot.configuration ["singleboost"]);
				double talkratio = (talkadd*targetratio + selftalk + quiet) / (talkadd + othertalk + totalboost);
				talkratio /= targetratio;
				talkProb /= Math.Pow(talkratio, 8) + 0.00001;
				double talkthr = Convert.ToDouble(Bot.configuration["talkthr"]);
				double talkthrdiv = Convert.ToDouble(Bot.configuration["talkthrdiv"]);
				double talkthrottle = selftalk /* / targetratio */ + othertalk - talkthr;
				if (talkthrottle > 0) talkProb /= Math.Exp((talkthrottle)/(talkthrdiv));
				if (talkProb > 1) talkProb = 1;
				if (thinking) talkProb = 0;
				message = "tHear=" + timeHeard.ToString("n2") + " tTalk=" + timeTalked.ToString("n2") + 
					" ts=" + timeSourceChange.ToString("n2") + " boost=" + totalboost.ToString("n0") +
				        " quiet=" + quiet.ToString() + " oTalk=" + othertalk.ToString("n2") + " sTalk=" + selftalk.ToString("n2") +
				        " ratio=" + talkratio.ToString("n2") + " prob=" + (talkProb*100).ToString("n2") + "%";
				if (Convert.ToInt32(Bot.configuration["talkinfo"]) > 0)
					                                     Console.WriteLine(message);
				Console.Title = message;
				if ((quiet == 0 && titler_last_update > 0) || Math.Abs(quiet - titler_last_update) >= 60)
					updateTitle();
			}
			logfile.WriteLine(message); logfile.Flush();

			if (Bot.rand.NextDouble() < talkProb)
			{
				lock(lck) thinking = true;
				NNInterfaceNew.getInterface(nnkey).getLine((s) =>
				{
					lock (lck)
					{
						selftalk += s.Length;
						thinking = false;
						boost *= Convert.ToDouble(Bot.configuration["boostdecay"]);
					}
					if (s != "")
						talk(s);
				});
				lastTalked = DateTime.Now;
			}
		}
	}
}
