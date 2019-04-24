using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using OpenMetaverse;

namespace NNBot
{
	public class DatabaseWriter
	{
		private MySqlConnection conn;
		private string connstring;
		private delegate void QueuedQuery();
		private BlockingCollection<QueuedQuery> queue = new BlockingCollection<QueuedQuery> ();
		private Task writerTask;

		public DatabaseWriter (string cs)
		{
			connstring = cs;
			reconnect (false);
			runWriter ();
		}

		private void reconnect(bool retry)
		{
			conn = new MySql.Data.MySqlClient.MySqlConnection ();
			conn.ConnectionString = connstring;
			while (true) {
				try {
					conn.Open();
					if (Convert.ToInt32(Bot.configuration["mysqlhack"]) > 0)
					{
						var cmd = conn.CreateCommand();
						cmd.CommandText = "SET NAMES utf8mb4;";
						cmd.ExecuteNonQuery();
					}
					return;
				} catch (MySql.Data.MySqlClient.MySqlException ex) {
					System.Console.WriteLine ("Mysql connect error: " + ex.Message);
					if (!retry)
						throw new Exception ("Database connection error", ex);
					Thread.Sleep (3000);
				}
			}
		}

		private void runWriter()
		{
			writerTask = Task.Run (() => {
				while(true) {
					QueuedQuery q = queue.Take();
					while(true) {
						try {
							q();
							break;
						} catch (MySql.Data.MySqlClient.MySqlException ex) {
							System.Console.WriteLine("Query error: " + ex.Message);
							conn.Close();
							Thread.Sleep (3000);
							reconnect(true);
						}
					}
				}
			});
		}

		private void logChat(string region, int type, string agentname, UUID agentuuid, string agentdisplayname, string botname, string message)
		{
			logChat (region, type, agentname, agentuuid, agentdisplayname, UUID.Zero, null, botname, message);
		}

		private void logChat(string region, int type, string agentname, UUID agentuuid, string agentdisplayname, UUID objectuuid, string objectname, string botname, string message)
		{
			MySqlCommand cmd = conn.CreateCommand();
			cmd.CommandText = "INSERT INTO chat(region, type, agentname, agentuuid, agentdisplayname, objectuuid, objectname, botname, message)" + 
				"VALUES(?region, ?type, ?agentname, ?agentuuid, ?agentdisplayname, ?objectuuid, ?objectname, ?botname, ?message)";
			cmd.Parameters.Add("?region", MySqlDbType.VarChar).Value = region;
			cmd.Parameters.Add("?type", MySqlDbType.Int16).Value = type;
			cmd.Parameters.Add("?agentname", MySqlDbType.VarChar).Value = agentname;
			cmd.Parameters.Add("?agentuuid", MySqlDbType.VarChar).Value = agentuuid.ToString();
			cmd.Parameters.Add("?agentdisplayname", MySqlDbType.VarChar).Value = agentdisplayname;
			if (objectname != null) {
				cmd.Parameters.Add("?objectuuid", MySqlDbType.VarChar).Value = objectuuid.ToString();
				cmd.Parameters.Add("?objectname", MySqlDbType.VarChar).Value = objectname;
			} else {
				cmd.Parameters.AddWithValue("?objectuuid", DBNull.Value);
				cmd.Parameters.AddWithValue("?objectname", DBNull.Value);
			}
			cmd.Parameters.Add("?botname", MySqlDbType.VarChar).Value = botname;
			cmd.Parameters.Add("?message", MySqlDbType.VarChar).Value = message;
			cmd.ExecuteNonQuery();
		}

		public void logChatEvent(ChatEventArgs e)
		{
			bool fromObject = (e.OwnerID != e.SourceID);
			string region = Bot.Client?.Network?.CurrentSim?.Name ?? "<unknown>";
			if (region == "<unknown>") Console.WriteLine("Can't get region name");
			if (fromObject) {
				NameCache.requestName (e.OwnerID);
				queue.Add (() => {
					string agentname = NameCache.getName(e.OwnerID);
					logChat(region, (int)e.Type, agentname, e.OwnerID, "", e.SourceID, e.FromName, Bot.Client.Self.Name, e.Message);
				});
			} else {
				queue.Add (() => {
					logChat (region, (int)e.Type, e.FromName, e.SourceID, "", Bot.Client.Self.Name, e.Message);
				});
			}
		}

		public void logObjectIM(InstantMessageEventArgs e, UUID owner)
		{
			string region = Bot.Client.Network.CurrentSim.Name;
			queue.Add (() => {
				logChat (region, -(int)e.IM.Dialog, NameCache.getName (owner), owner, "", e.IM.FromAgentID, e.IM.FromAgentName, Bot.Client.Self.Name, e.IM.Message);
			});
		}

		private void logIM(string region, int type, string fromname, UUID fromuuid, string fromdisplayname, string botname, UUID sessionid, string message)
		{
			MySqlCommand cmd = conn.CreateCommand ();
			cmd.CommandText = "INSERT INTO im(region, type, fromname, fromuuid, fromdisplayname, botname, sessionid, message)" +
				"VALUES(?region, ?type, ?fromname, ?fromuuid, ?fromdisplayname, ?botname, ?sessionid, ?message)";
			cmd.Parameters.AddWithValue ("?region", region);
			cmd.Parameters.AddWithValue ("?type", type);
			cmd.Parameters.AddWithValue ("?fromname", fromname);
			cmd.Parameters.AddWithValue ("?fromuuid", fromuuid.ToString());
			cmd.Parameters.AddWithValue ("?fromdisplayname", fromdisplayname);
			cmd.Parameters.AddWithValue ("?botname", botname);
			cmd.Parameters.AddWithValue("?sessionid", sessionid);
			cmd.Parameters.AddWithValue ("?message", message);
			cmd.ExecuteNonQuery ();
		}

		public void logSentIM(UUID to, UUID sessionid, string name, string message)
		{
			string region = Bot.Client.Network.CurrentSim.Name;
			queue.Add (() => {
				logIM(region, -100, name, to, "", Bot.Client.Self.Name, sessionid, message);
			});
		}

		public void logIMEvent(InstantMessageEventArgs e)
		{
			string region = Bot.Client.Network.CurrentSim.Name;
			queue.Add (() => {
				logIM(region, (int)e.IM.Dialog, e.IM.FromAgentName, e.IM.FromAgentID, "", Bot.Client.Self.Name, e.IM.IMSessionID, e.IM.Message);
			});
		}
	}
}

