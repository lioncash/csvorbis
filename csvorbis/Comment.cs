/* csvorbis
 * Copyright (C) 2000 ymnk, JCraft,Inc.
 *  
 * Written by: 2000 ymnk<ymnk@jcraft.com>
 * Ported to C# from JOrbis by: Mark Crichton <crichton@gimp.org> 
 *   
 * Thanks go to the JOrbis team, for licencing the code under the
 * LGPL, making my job a lot easier.
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Library General Public License
 * as published by the Free Software Foundation; either version 2 of
 * the License, or (at your option) any later version.
   
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Library General Public License for more details.
 * 
 * You should have received a copy of the GNU Library General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
 */


using System;
using System.Collections.Generic;
using System.Text;
using csogg;

namespace csvorbis 
{
	/// <summary>
	/// Defines an Ogg Vorbis comment.
	/// </summary>
	public class Comment
	{
		private const string _vorbis = "vorbis";

		// unlimited user comment fields.  libvorbis writes 'libvorbis'
		// whatever vendor is set to in encode
		private byte[][] user_comments;
		private int[] comment_lengths; 
		private int comments;
		private byte[] vendor;

		public void init()
		{
			user_comments = null;
			comments = 0;
			vendor = null;
		}

		public void add(string comment)
		{
			Encoding AE = Encoding.UTF8;
			byte[] comment_byt = AE.GetBytes(comment);
			add(comment_byt);
		}

		private void add(byte[] comment)
		{
			byte[][] foo = new byte[comments + 2][];
			if (user_comments != null)
			{
				Array.Copy(user_comments, 0, foo, 0, comments);
			}
			user_comments = foo;

			int[] goo = new int[comments + 2];
			if (comment_lengths != null)
			{
				Array.Copy(comment_lengths, 0, goo, 0, comments);
			}
			comment_lengths = goo;

			byte[] bar = new byte[comment.Length + 1];
			Array.Copy(comment, 0, bar, 0, comment.Length);
			user_comments[comments] = bar;
			comment_lengths[comments] = comment.Length;
			comments++;
			user_comments[comments] = null;
		}

		public void add_tag(string tag, string contents)
		{
			if (contents == null)
				contents = "";

			add(tag + "=" + contents);
		}

		// This is more or less the same as strncasecmp - but that doesn't exist
		// everywhere, and this is a fairly trivial function, so we include it
		private static bool tagcompare(byte[] s1, byte[] s2, int n)
		{
			for (int c = 0; c < n; c++)
			{
				byte u1 = s1[c];
				byte u2 = s2[c];

				if (u1 >= 'A')
					u1 = (byte) (u1 - 'A' + 'a');

				if (u2 >= 'A')
					u2 = (byte) (u2 - 'A' + 'a');

				if (u1 != u2)
				{
					return false;
				}
			}

			return true;
		}

		public string query(string tag)
		{
			return query(tag, 0);
		}

		public string query(string tag, int count)
		{
			Encoding AE = Encoding.UTF8;
			byte[] tag_byt = AE.GetBytes(tag);

			int foo = query(tag_byt, count);
			if (foo == -1) return null;
			byte[] comment = user_comments[foo];

			for (int i = 0; i < comment_lengths[foo]; i++)
			{
				if (comment[i] == '=')
				{
					char[] comment_uni = AE.GetChars(comment);

					return new string(comment_uni, i + 1, comment_lengths[foo] - (i + 1));
				}
			}

			return null;
		}

		private int query(byte[] tag, int count)
		{
			int found = 0;
			int taglen = tag.Length;
			byte[] fulltag = new byte[taglen + 2];
			Array.Copy(tag, 0, fulltag, 0, tag.Length);
			fulltag[tag.Length] = (byte) '=';

			for (int i = 0; i < comments; i++)
			{
				if (tagcompare(user_comments[i], fulltag, taglen))
				{
					if (count == found)
					{
						// We return a pointer to the data, not a copy
						//return user_comments[i] + taglen + 1;
						return i;
					}
					else
					{
						found++;
					}
				}
			}

			return -1;
		}

		internal int unpack(csBuffer opb)
		{
			int vendorlen = opb.read(32);
			if (vendorlen < 0)
			{
				//goto err_out;
				clear();
				return -1;
			}

			vendor = new byte[vendorlen + 1];
			opb.read(vendor, vendorlen);

			comments = opb.read(32);
			if (comments < 0)
			{
				clear();
				return -1;
			}
			user_comments = new byte[comments + 1][];
			comment_lengths = new int[comments + 1];

			for (int i = 0; i < comments; i++)
			{
				int len = opb.read(32);
				if (len < 0)
				{
					clear();
					return -1;
				}
				comment_lengths[i] = len;
				user_comments[i] = new byte[len + 1];
				opb.read(user_comments[i], len);
			}

			if (opb.read(1) != 1)
			{
				// EOP check
				clear();
				return -1;

			}
			return 0;
		}

		private int pack(csBuffer opb)
		{
			const string temp = "Xiphophorus libVorbis I 20000508";

			Encoding AE = Encoding.UTF8;
			byte[] temp_byt = AE.GetBytes(temp);
			byte[] _vorbis_byt = AE.GetBytes(_vorbis);

			// preamble
			opb.write(0x03, 8);
			opb.write(_vorbis_byt);

			// vendor
			opb.write(temp.Length, 32);
			opb.write(temp_byt);

			// comments
			opb.write(comments, 32);
			if (comments != 0)
			{
				for (int i = 0; i < comments; i++)
				{
					if (user_comments[i] != null)
					{
						opb.write(comment_lengths[i], 32);
						opb.write(user_comments[i]);
					}
					else
					{
						opb.write(0, 32);
					}
				}
			}

			opb.write(1, 1);

			return 0;
		}

		public int header_out(Packet op)
		{
			csBuffer opb = new csBuffer();
			opb.writeinit();

			if (pack(opb) != 0)
				return VorbisFile.OV_EIMPL;

			op.packet_base = new byte[opb.bytes()];
			op.packet = 0;
			op.bytes = opb.bytes();
			Array.Copy(opb.buf(), 0, op.packet_base, 0, op.bytes);
			op.b_o_s = 0;
			op.e_o_s = 0;
			op.granulepos = 0;
			return 0;
		}

		internal void clear()
		{
			for (int i = 0; i < comments; i++)
				user_comments[i] = null;

			user_comments = null;
			vendor = null;
		}

		public string getVendor()
		{
			if (vendor == null)
				return null;

			return Encoding.UTF8.GetString(vendor);
		}

		public string getComment(int i)
		{
			if (comments <= i)
				return null;
			
			return Encoding.UTF8.GetString(user_comments[i]);
		}

		public List<String> getAllComments()
		{
			List<String> comments = new List<string>();

			// Empty list if no comments are initialized.
			if (user_comments == null)
				return comments;

			// Convert all of the comments to strings.
			foreach (byte[] userComment in user_comments)
			{
				if (userComment != null)
				{
					comments.Add(Encoding.UTF8.GetString(userComment));
				}
			}

			return comments;
		}

		public override string ToString()
		{
			Encoding AE = Encoding.UTF8;
			string long_string = "Vendor: " + AE.GetString(vendor);

			for (int i = 0; i < comments; i++)
				long_string = long_string + "\nComment: " + AE.GetString(user_comments[i]);

			long_string = long_string + "\n";

			return long_string;
		}
	}
}
