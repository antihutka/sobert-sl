using System;
using OpenMetaverse;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;

namespace NNBot
{
	public class Groups
	{
		public Groups()
		{
		}

		static object lck = new object();
		static Dictionary<UUID, GroupMember> cached = null;
		static Stopwatch timer;

		public static bool isInGroup(UUID usr, UUID gr)
		{
			lock(lck)
			{
				Thread.Sleep(2000);
				if (cached == null || timer.ElapsedMilliseconds > 5 * 60 * 1000)
				{
					var q = new BlockingCollection<bool>();
					EventHandler<GroupMembersReplyEventArgs> handler = null;
					handler = (sender, e) =>
					{
						q.Add(e.Members.ContainsKey(usr));
						cached = e.Members;
						timer = Stopwatch.StartNew();
					};
					Bot.Client.Groups.GroupMembersReply += handler;
					Bot.Client.Groups.RequestGroupMembers(gr);
					bool result = false;
					q.TryTake(out result, 15 * 1000);
					Bot.Client.Groups.GroupMembersReply -= handler;
					return result;
				} else {
					return cached.ContainsKey(usr);
				}
			}
		}
	}
}
