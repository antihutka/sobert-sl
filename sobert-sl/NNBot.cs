using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using OpenMetaverse.Packets;

namespace NNBot
{
	public class Bot
	{
		public delegate void Reply(string s);

		public static GridClient Client;
		public static Dictionary<String, String> configuration;
		public static DatabaseWriter dbw;
		public static Random rand = new Random();
		static ConversationHandler localchat;
		static UUID lastsit = UUID.Zero;

		public static void Main(string[] args)
		{
			string configfile = "default.cfg";
			if (args.Length > 0)
				configfile = args[0];
			configuration = Config.LoadConfig(configfile);
			//Console.WriteLine(string.Join(";", configuration));

			dbw = new DatabaseWriter(configuration["dbstring"]);

			Client = new GridClient();
			Client.Settings.LOG_DISKCACHE = false;
			Settings.LOG_LEVEL = Helpers.LogLevel.Info;
			if (configuration["loginserver"] != "") Client.Settings.LOGIN_SERVER = configuration["loginserver"];
			Client.Self.IM += new EventHandler<InstantMessageEventArgs>(IMHandler);
			Client.Self.ChatFromSimulator += new EventHandler<ChatEventArgs>(ChatHandler);
			Client.Avatars.UUIDNameReply += new EventHandler<UUIDNameReplyEventArgs>(UUIDNameHandler);
			Client.Self.ScriptQuestion += new EventHandler<ScriptQuestionEventArgs>(PermissionRequestHandler);
			Client.Groups.GroupJoinedReply += Groups_GroupJoinedReply;
			Client.Groups.GroupInvitation += Groups_GroupInvitation;
			Reply talklocal = (string s) =>
			{
				if (Client.Network.CurrentSim.Access == SimAccess.PG && Convert.ToInt32(configuration["nogtalk"]) > 0)
				{
					Console.WriteLine("Chat message blocked because of sim rating: " + s);
					return;
				}
				Client.Self.Chat(s, 0, ChatType.Normal);
			};
			localchat = new ConversationHandler("<local>", talklocal);
			//debugEvents ();
			if (configuration.ContainsKey("throttle"))
			{
				Client.Throttle.Total = Convert.ToSingle(configuration["throttle"]);
			}
			Console.WriteLine("throttle set to: Ass {0}, Cld {1}, Lnd {2}, Rsn {3}, Tsk {4}, Txt {5}, Wnd {6} Total {7}", Client.Throttle.Asset, Client.Throttle.Cloud, Client.Throttle.Land,
							  Client.Throttle.Resend, Client.Throttle.Task, Client.Throttle.Texture, Client.Throttle.Wind, Client.Throttle.Total);
			bool loggedIn = Client.Network.Login(configuration["firstname"], configuration["lastname"], configuration["password"], "NNBot", "NNBot 0.1");
			if (loggedIn)
			{
				Console.WriteLine("Logged In");
				localchat.start();
				Thread.Sleep(3000);
				attachStuff();
				Client.Self.RetrieveInstantMessages();
				Task.Run(() => consoleCommands());
				doLoginTeleport ();
				doLoginSit();
			}
			else Console.WriteLine("Failed");
		}

		private static void doLoginTeleport()
		{
			while (true) {
				string region = Client.Network.CurrentSim.Name;
				System.Console.WriteLine("Current sim: " + region);
				string k = "tpfrom:" + region;
				if (configuration.ContainsKey (k)) {
					string tpto = configuration [k];
					System.Console.WriteLine ("Trying to teleport to: " + tpto);
					Client.Self.Teleport (tpto, new Vector3 (128.0f, 128.0f, 128.0f));
					Thread.Sleep (30000);
				} else {
					return;
				}
			}
		}

		private static void doLoginSit()
		{
			UUID siton = UUID.Zero;
			if (configuration.ContainsKey("loginsit") && UUID.TryParse(configuration["loginsit"], out siton))
			{
				Thread.Sleep(6000);
				Client.Self.RequestSit(siton, Vector3.Zero);
				Client.Self.Sit();
				Console.WriteLine("sitting on " + siton);
			}
		}

		private static void debugEvents()
		{
			Client.Objects.ObjectProperties += new EventHandler<ObjectPropertiesEventArgs>((object sender, ObjectPropertiesEventArgs e) => { Console.WriteLine("ObjectProperties for " + e.Properties.ObjectID); });
			Client.Objects.ObjectPropertiesUpdated += new EventHandler<ObjectPropertiesUpdatedEventArgs>((object sender, ObjectPropertiesUpdatedEventArgs e) => { Console.WriteLine("ObjectPropertiesUpdated for " + e.Prim.ID); });
			Client.Objects.TerseObjectUpdate += new EventHandler<TerseObjectUpdateEventArgs>((object sender, TerseObjectUpdateEventArgs e) => { Console.WriteLine("TerseObjectUpdate for " + e.Prim.ID); });
			Client.Objects.ObjectPropertiesFamily += new EventHandler<ObjectPropertiesFamilyEventArgs>((object sender, ObjectPropertiesFamilyEventArgs e) => { Console.WriteLine("ObjectPropertiesFamilyEventArgs for " + e.Properties.ObjectID); });
			Client.Objects.ObjectDataBlockUpdate += new EventHandler<ObjectDataBlockUpdateEventArgs>((object sender, ObjectDataBlockUpdateEventArgs e) => { Console.WriteLine("ObjectDataBlockUpdateEventArg for " + e.Prim.ID); });
		}

		private static void UUIDNameHandler(object sender, UUIDNameReplyEventArgs e)
		{
			foreach (UUID id in e.Names.Keys)
				NameCache.recvName(id, e.Names[id]);
		}

		private static void PermissionRequestHandler(object sender, ScriptQuestionEventArgs e)
		{
			Console.WriteLine("Permission request: " + e.ObjectOwnerName + "/" + e.ObjectName + ":" + e.Questions);
			Client.Self.ScriptQuestionReply(e.Simulator, e.ItemID, e.TaskID, ScriptPermission.Attach | ScriptPermission.ControlCamera | ScriptPermission.TakeControls | ScriptPermission.TrackCamera | ScriptPermission.RemapControls | ScriptPermission.TriggerAnimation);
		}

		private static void ChatHandler(object sender, ChatEventArgs e)
		{
			bool log = true;
			switch (e.Type)
			{
				case ChatType.StopTyping:
				case ChatType.StartTyping:
				case ChatType.Debug:
					log = false;
					break;
				case ChatType.Normal:
				case ChatType.Whisper:
				case ChatType.Shout:
				case ChatType.RegionSayTo:
					if (e.Type == ChatType.RegionSayTo && e.Message.StartsWith("/!"))
					{
						string ownerName = NameCache.getName(e.OwnerID);
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine("[UserCommand][" + ownerName + "/" + e.FromName + "] " + e.Message);
						Console.ResetColor();
						processCommand(e.Message.Substring(2), userAccessLevel(ownerName), (s) => Console.WriteLine(s), e.SourceID);
						log = false;
					}
					else {
						bool ignored = isinlist(configuration["ignoreobjectchat"], e.SourceID.ToString());
						if (e.Message.Equals(""))
						{
							log = false;
							break;
						}
						if (!ignored)
						{
							if (e.SourceID == Client.Self.AgentID) Console.ForegroundColor = ConsoleColor.Yellow;
							Console.WriteLine("[local][" + e.FromName + "] " + e.Message);
							Console.ResetColor();
						}
						if (e.SourceID != Client.Self.AgentID && !ignored)
						{
							string msg = e.Message;
							string src = e.FromName;
							if (e.FromName == ">" && msg.StartsWith("/me [secondlife://") && msg.Contains("]: "))
							{
								if (msg.Contains("about "))
								{
									src = msg.Substring(msg.IndexOf("about ") + 6);
									src = src.Substring(0, src.IndexOf("]"));
								}
								msg = msg.Substring(msg.IndexOf("]: ") + 3);
							}
							localchat.incomingMessage(msg, false, src);
						}
					}
					break;
				default:
					Console.WriteLine("Unknown chat type " + e.Type + " from " + e.FromName + ":" + e.Message);
					break;
			}

			if (log)
			{
				dbw.logChatEvent(e);
			}
		}

		private static void attachStuff()
		{
			UUID co = Client.Inventory.FindFolderForType(FolderType.CurrentOutfit);
			Console.WriteLine("Current outfit folder: " + co);
			Thread.Sleep(1000);
			Console.WriteLine("Getting folder contents");
			var coc = Client.Inventory.FolderContents(co, Client.Self.AgentID, true, true, InventorySortOrder.ByDate, 30000);
			Console.WriteLine("Got inventory contents");
			Thread.Sleep(1000);
			if (coc == null)
			{
				Console.WriteLine("FolderContents failed");
				return;
			}
			List<InventoryItem> coitems = new List<InventoryItem>();
			Console.WriteLine("length: " + coc.Count + "/" + coitems.Count);
			coc.ForEach((InventoryBase obj) =>
			{
				if (obj is InventoryObject)
				{
					Console.WriteLine("Adding " + obj.Name);
					obj = Client.Inventory.FetchItem(((InventoryObject)obj).AssetUUID, Client.Self.AgentID, 10000);
					Client.Appearance.Attach((InventoryObject)obj, AttachmentPoint.Default, false);
					Thread.Sleep(1000);
					coitems.Clear();
				}
			});
		}

		static void Groups_GroupInvitation(object sender, GroupInvitationEventArgs e)
		{
			Console.WriteLine("Group invitation from " + e.FromName + ": " + e.Message);
		}

		static void Groups_GroupJoinedReply(object sender, GroupOperationEventArgs e)
		{
			Console.WriteLine("Group join reply: " + e.GroupID + " => " + e.Success);
		}

		public static Primitive findObjectInSim(UUID id)
		{
			return Client.Network.CurrentSim.ObjectsPrimitives.Find((Primitive obj) => obj.ID == id);
		}

		private static void consoleCommands()
		{
			while (true)
			{
				try
				{
					string l = Console.ReadLine();
					Task.Run(() => processCommand(l, 200, (s) => Console.WriteLine(s), UUID.Zero));
					Thread.Sleep(50);
				}
				catch (Exception e)
				{
					System.Console.WriteLine(e);
				}
			}
		}

		static void IMHandler(object sender, InstantMessageEventArgs e)
		{
			bool log = true;
			switch (e.IM.Dialog)
			{
				case InstantMessageDialog.StartTyping:
				case InstantMessageDialog.StopTyping:
					log = false;
					break;
				case InstantMessageDialog.RequestLure:
					Console.WriteLine("Teleport request from " + e.IM.FromAgentName + " , sending offer");
					if (userAccessLevel(e.IM.FromAgentName) < 1) Thread.Sleep(rand.Next(5000, 15000));
					Client.Self.SendTeleportLure(e.IM.FromAgentID);
					break;
				case InstantMessageDialog.RequestTeleport:
					Console.WriteLine("Teleport offer from " + e.IM.FromAgentName);
					if (userAccessLevel(e.IM.FromAgentName) >= commandAccessLevel("teleport"))
						Client.Self.TeleportLureRespond(e.IM.FromAgentID, e.IM.IMSessionID, true);
					break;
				case InstantMessageDialog.FriendshipOffered:
					Console.WriteLine("Friendship request from " + e.IM.FromAgentName + ": " + e.IM.Message);
					Thread.Sleep(rand.Next(5000, 15000));
					Client.Friends.AcceptFriendship(e.IM.FromAgentID, e.IM.IMSessionID);
					Thread.Sleep(5000);
					Client.Friends.GrantRights(e.IM.FromAgentID, FriendRights.CanSeeOnMap | FriendRights.CanSeeOnline);
					break;
				case InstantMessageDialog.MessageFromAgent:
					if (e.IM.ToAgentID != Client.Self.AgentID)
					{
						Console.WriteLine("[group][" + e.IM.ToAgentID + "][" + e.IM.FromAgentName + "] " + e.IM.Message);
					}
					/*else if (isOwner(e.IM.FromAgentName))
					{
						Console.WriteLine("[command][" + e.IM.FromAgentName + "] " + e.IM.Message);
						Reply reply = s =>
							//Client.Self.InstantMessage (e.IM.FromAgentID, s, e.IM.IMSessionID);
							//Thread.Sleep(3000);
							Console.WriteLine(s);
						processCommand(e.IM.Message, 100, reply, e.IM.FromAgentID);
						log = false;
					}*/
					else if (e.IM.FromAgentName.Equals("Second Life"))
					{
						Console.WriteLine("[UserOffline] " + NameCache.getName(e.IM.FromAgentID));
						log = false;
					}
					else if (e.IM.Message.StartsWith("/!"))
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine("[UserCommand][" + e.IM.FromAgentName + "] " + e.IM.Message);
						Console.ResetColor();
						// processCommand(e.IM.Message.Substring(2), userAccessLevel(e.IM.FromAgentName), (s) => Console.WriteLine(s), e.IM.FromAgentID);
						//string cmdout = "";
						//processCommand(e.IM.Message.Substring(2), userAccessLevel(e.IM.FromAgentName), (s) => cmdout = cmdout + s + "\n", e.IM.FromAgentID);
						//Client.Self.InstantMessage(e.IM.FromAgentID, cmdout, e.IM.IMSessionID);
						processCommand(e.IM.Message.Substring(2), userAccessLevel(e.IM.FromAgentName), (s) => Client.Self.InstantMessage(e.IM.FromAgentID, s, e.IM.IMSessionID), e.IM.FromAgentID);
						log = false;
					}
					else {
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine("[IM <- " + e.IM.FromAgentName + "] " + e.IM.Message);
						Console.ResetColor();
						dbw.logIMEvent(e);
						log = false;
						var nni = NNInterfaceNew.getInterface(e.IM.FromAgentName);
						nni.pushLine(e.IM.Message);
						nni.getLine((s) =>
						{
							if (s.Length == 0)
							{
								Console.WriteLine("Tried to send empty IM to " + e.IM.FromAgentName);
								return;
							}
							Client.Self.InstantMessage(e.IM.FromAgentID, s, e.IM.IMSessionID);
							Console.ForegroundColor = ConsoleColor.Cyan;
							Console.WriteLine("[IM -> " + e.IM.FromAgentName + "] " + s);
							Console.ResetColor();
							dbw.logSentIM(e.IM.FromAgentID, e.IM.IMSessionID, e.IM.FromAgentName, s);
						});
					}
					break;
				case InstantMessageDialog.MessageFromObject:
					Primitive o = findObjectInSim(e.IM.FromAgentID);
					UUID ownerid = (o == null) ? UUID.Zero : ObjPropGetter.getProperties(o).OwnerID;
					bool ignored = isinlist(configuration["ignoreobjectchat"], e.IM.FromAgentID.ToString());
					string owner = NameCache.getName(ownerid);
					Console.WriteLine("[object][" + (ignored ? "ign][" : "") + owner + "][" + e.IM.FromAgentName + "] " + e.IM.Message);
					if (!ignored)
					{
						string msg = e.IM.Message;
						if (e.IM.FromAgentName == "." && msg.Contains(": "))
							msg = msg.Substring(msg.IndexOf(": ") + 2);
						localchat.incomingMessage(msg, (lastsit == e.IM.FromAgentID), e.IM.FromAgentName);
					}
					break;
				case InstantMessageDialog.SessionSend:
					Console.WriteLine("[session][" + e.IM.FromAgentName + "] " + e.IM.Message);
					break;
				default:
					Console.WriteLine("Unknown IM type " + e.IM.Dialog + " from " + e.IM.FromAgentName + ": " + e.IM.Message);
					break;
			}
			if (log)
			{
				dbw.logIMEvent(e);
			}
		}

		private static void listInventory(UUID folder, bool recurse, Reply reply, string search)
		{
			List<InventoryBase> cont = Client.Inventory.FolderContents(folder, Client.Self.AgentID, true, true, InventorySortOrder.ByDate, 10000);
			string messagec = "";
			cont.ForEach((InventoryBase item) =>
			{
				string message = item.Name + " " + item.GetType() + " " + item.UUID;
				if (item is InventoryItem) message += " " + ((InventoryItem)item).AssetUUID;
				if (search != null && !message.ToLower().Contains(search.ToLower())) return;
				messagec += message + "\n";
				if (messagec.Length > 512)
				{
					reply(messagec);
					messagec = "";
				}
			});
			if (messagec != "") reply(messagec);
			if (recurse)
			{
				cont.ForEach((InventoryBase item) =>
				{
					if (item is InventoryFolder)
						listInventory(item.UUID, true, reply, search);
				});
			}
			if (!recurse && cont.Count == 0)
				reply("(empty)");
		}

		private static void split(string str, string spliton, out string first, out string last)
		{
			var i = str.IndexOf(spliton);
			if (i >= 0)
			{
				first = str.Substring(0, i).Trim();
				last = str.Substring(i + 1).Trim();
			}
			else {
				first = str.Trim();
				last = "";
			}
		}

		private static int commandAccessLevel(string command)
		{
			switch (command)
			{
				case "quiet":
				case "invite":
					return 0;
				case "help":
					return 1;
				case "nearby":
				case "objects":
				case "status":
				case "sit":
				case "stand":
				case "touch":
					return 50;
				case "attachstuff":
				case "say":
				case "teleport":
				case "shout":
				case "whisper":
				case "csay":
					return 60;
				case "setgroup":
				case "attach":
				case "detach":
				case "inventory":
				case "joingroup":
					return 80;
				default:
					return 100;
			}
		}

		private static int userAccessLevel(string username)
		{
			string al;
			if (configuration.TryGetValue("access:" + username, out al))
				return int.Parse(al);
			else
				return 0;
		}

		private static void processCommand(string command, int accesslevel, Reply reply, UUID from)
		{
			command = command.Trim();
			if (command == "") return;
			var i = command.IndexOf(" ");
			string c, a = "";
			char[] slash = { '/' };
			split(command, " ", out c, out a);

			Vector3 selfpos;
			Client.Network.CurrentSim.AvatarPositions.TryGetValue(Client.Self.AgentID, out selfpos);

			if (accesslevel < commandAccessLevel(c))
			{
				Console.WriteLine("Bad accesslevel " + accesslevel.ToString() + " for command <" + c + ">");
				return;
			}

			switch (c)
			{
			case "help":
				reply("Commands: help inventory logout nearby objects [attach child near] say shout sit stand status whisper");
				break;
			case "attach":
				var l = Client.Inventory.FetchItem(UUID.Parse(a), Client.Self.AgentID, 10000);
				Console.WriteLine("Attaching " + l.Name);
				Client.Appearance.Attach((InventoryItem)l, AttachmentPoint.Default, false);
				break;
			case "attachstuff":
				attachStuff();
				break;
			case "detach":
				l = Client.Inventory.FetchItem(UUID.Parse(a), Client.Self.AgentID, 10000);
				Console.WriteLine("Dettaching " + l.Name);
				Client.Appearance.Detach((InventoryItem)l);
				break;
			case "inventory":
				UUID folderid;
				if (a == "")
				{
					listInventory(Client.Inventory.Store.RootFolder.UUID, false, reply, null);
				}
				else if (UUID.TryParse(a, out folderid))
				{
					listInventory(folderid, false, reply, null);
				}
				else {
					listInventory(Client.Inventory.Store.RootFolder.UUID, true, reply, a);
				}
				break;
			case "invite":
				UUID invid;
				if (!UUID.TryParse(a, out invid)) invid = from;
				if (Groups.isInGroup(invid, UUID.Parse(configuration["invitegroup"])))
				{
					Console.WriteLine("User " + NameCache.getName(invid) + " already in group");
				} else {
					Client.Groups.Invite(UUID.Parse(configuration["invitegroup"]), new List<UUID> { UUID.Zero }, invid);
					Console.WriteLine("Inviting " + invid + "/" + NameCache.getName(invid));
				}
				break;
			case "joingroup":
				UUID joinid;
				if (UUID.TryParse(a, out joinid))
				{
					Client.Groups.RequestJoinGroup(joinid);
				}
				break;
			case "logout":
				Client.Network.Logout();
				break;
			case "nearby":
				Client.Network.CurrentSim.ObjectsAvatars.ForEach(av =>
			   {
				   if (av.ID == Client.Self.AgentID) return;
				   Vector3 position = Vector3.Zero;
				   Client.Network.CurrentSim.AvatarPositions.TryGetValue(av.ID, out position);
				   string message = av.Name + " @ " + position + " " + (selfpos - position).Length() + "m";
				   reply(message);
			   });
				break;
			case "objects":
				var args = a.Split();
				bool attach = Array.IndexOf(args, "attach") >= 0;
				bool child = Array.IndexOf(args, "child") >= 0;
				bool near = Array.IndexOf(args, "near") >= 0;
				bool own = Array.IndexOf(args, "own") >= 0;

				var prims = new List<Primitive>();
				Client.Network.CurrentSim.ObjectsPrimitives.ForEach((Primitive prim) =>
				{
					prims.Add(prim);
				});

				string messagec = "";
				prims.ForEach((Primitive prim) =>
				{
					if (prim.ID == UUID.Zero)
						return;
					if (attach != prim.IsAttachment)
						return;
					if (!child && !attach && prim.ParentID != 0)
						return;

					double distance = (selfpos - prim.Position).Length();

					if (near && distance > 20)
						return;

					string message = prim.ID.ToString();
					message += " parent=" + prim.ParentID;
					message += " dist=" + distance.ToString("#.00") + "m";

					Primitive.ObjectProperties prop = ObjPropGetter.getProperties(prim);
					if (prop != null)
					{
						message += " name=" + prop.Name;
						message += " owner=" + NameCache.getName(prop.OwnerID);
					}
					else
						message += " <timed out>";
					if (own && prop.OwnerID != Client.Self.AgentID) return;
					messagec += message + "\n";
					if (messagec.Length > 512)
					{
						reply(messagec);
						messagec = "";
					}
				});
				if (messagec != "")
					reply(messagec);
				break;
			case "say":
				Client.Self.Chat(a, 0, ChatType.Normal);
				break;
			case "csay":
					string chan;
					string msg;
					split(a, " ", out chan, out msg);
					Client.Self.Chat(msg, Convert.ToInt32(chan), ChatType.Normal);
					break;
			case "set":
				string left, right;
				split(a, "=", out left, out right);
				configuration[left] = right;
				break;
			case "setgroup":
				UUID actgroup;
				if (UUID.TryParse(a, out actgroup))
				{
					Client.Groups.ActivateGroup(actgroup);
				}
				break;
			case "shout":
				Client.Self.Chat(a, 0, ChatType.Shout);
				break;
			case "sit":
				UUID arg = UUID.Zero;
				if (a == "" && configuration.ContainsKey ("loginsit"))
					a = configuration ["loginsit"];
				if (!UUID.TryParse(a, out arg))
				{
					reply("Invalid ID");
				}
				else {
					Client.Self.RequestSit(arg, Vector3.Zero);
					Client.Self.Sit();
					lastsit = arg;
				}
				break;
			case "stand":
				Client.Self.SignaledAnimations.ForEach((KeyValuePair<UUID, int> obj) => Client.Self.AnimationStop(obj.Key, true));
				Client.Self.Stand();
				break;
			case "status":
				reply("Location: " + Client.Network.CurrentSim.Name + " @ " + Client.Network.CurrentSim.AvatarPositions[Client.Self.AgentID]);
				reply("Avatars: " + Client.Network.CurrentSim.AvatarPositions.Count);
				reply("Objects:" + Client.Network.CurrentSim.ObjectsPrimitives.Count);
				break;
			case "teleport":
				UUID lm_uuid;
				if (UUID.TryParse(a, out lm_uuid))
				{
					Client.Self.Teleport(lm_uuid);
				}
				else
					Client.Self.Teleport(a, new Vector3(128.0f, 128.0f, 128.0f));

				break;
			case "touch":
				if (UUID.TryParse(a, out lm_uuid))
				{
					Primitive prim = Client.Network.CurrentSim.ObjectsPrimitives.Find((Primitive obj) => obj.ID == lm_uuid);
					if (prim != null)
					{
						Client.Self.Touch(prim.LocalID);
						reply("ok");
					}
				}
				break;
			case "whisper":
				Client.Self.Chat(a, 0, ChatType.Whisper);
				break;
			case "quiet":
				int q;
				localchat.setquiet(int.TryParse(a, out q) ? q : 5000, from == UUID.Zero ? "<console>" : NameCache.getName(from));
				break;
			default:
				reply("Unknown command ");
				break;
			}
		}

		static bool isinlist(string h, string n)
		{
			return h.ToLower().Contains("[" + n.ToLower() + "]");
		}
	}
}
