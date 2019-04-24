using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using OpenMetaverse;

namespace NNBot
{
	public class ObjPropGetter
	{
		public static Primitive.ObjectProperties getProperties(Primitive prim)
		{
			if (prim.Properties != null)
				return prim.Properties;
			uint id = prim.LocalID;
			var q = new BlockingCollection<Primitive.ObjectProperties> ();
			EventHandler<ObjectPropertiesEventArgs> handler = null;
			handler = new EventHandler<ObjectPropertiesEventArgs> ((object sender, ObjectPropertiesEventArgs e) => {
				//Console.WriteLine("Received properties for " + e.Properties.ObjectID);
				if (e.Properties.ObjectID == prim.ID)
					q.Add(e.Properties);
			});
			Bot.Client.Objects.ObjectProperties += handler;
			//Bot.Client.Objects.RequestObject (Bot.Client.Network.CurrentSim, prim.LocalID);
			Bot.Client.Objects.SelectObject (Bot.Client.Network.CurrentSim, prim.LocalID, true);
			//Console.WriteLine ("Requested properties for " + prim.ID + " (" + prim.LocalID + ")");
			Primitive.ObjectProperties prop = null;
			q.TryTake (out prop, 500);
			Bot.Client.Objects.ObjectProperties -= handler;
			return prop;
		}
	}
}
