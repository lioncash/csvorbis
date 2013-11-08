using System;
using System.Media;
using System.IO;

namespace OggDecoder
{
	/// <summary>
	/// Program for playing back OGG encoded files.
	/// </summary>
	internal class OggPlayer
	{
		private static void Main(string[] args)
		{
			if (args.Length > 0)
			{
				using (var file = new FileStream(args[0], FileMode.Open, FileAccess.Read))
				{
					var player = new SoundPlayer(new OggDecodeStream(file));
					player.PlaySync();
				}
			}
			else
			{
				Console.WriteLine("Invalid number of parameters passed.");
				Console.WriteLine("Should be: OggPlayer [path to valid OGG file]");
			}
		}
	}
}
