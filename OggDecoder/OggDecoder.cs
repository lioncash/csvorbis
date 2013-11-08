using System;
using System.IO;

namespace OggDecoder
{
	/// <summary>
	/// Ogg Vorbis decoder test application.
	/// </summary>
	internal class Decoder
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		//[STAThread]
		private static void Main(string[] args)
		{
			TextWriter s_err = Console.Error;
			FileStream input = null, output = null;

			if (args.Length == 2)
			{
				try
				{
					input = new FileStream(args[0], FileMode.Open, FileAccess.Read);
					output = new FileStream(args[1], FileMode.OpenOrCreate);
				}
				catch (Exception e)
				{
					s_err.WriteLine(e);
				}
			}
			else
			{
				Console.WriteLine("Invalid number of commands entered.");
				Console.WriteLine("Should resemble: OggDecoder [input] [output]");
				return;
			}

			OggDecodeStream decode = new OggDecodeStream(input, false);

			byte[] buffer = new byte[4096];
			int read;
			while ((read = decode.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, read);
			}

			// Close some files
			input.Close();
			output.Close();
		}
	}
}

