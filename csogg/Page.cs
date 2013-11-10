/* csogg
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

namespace csogg
{
	/// <summary>
	/// Used to encapsulate the data in one Ogg bitstream page.
	/// </summary>
	public sealed class Page
	{
		private static readonly uint[] crc_lookup = new uint[256];

		public int header;
		public int header_len;
		public byte[] header_base;

		public int body;
		public int body_len;
		public byte[] body_base;

		/// <summary>
		/// Constructor
		/// </summary>
		public Page()
		{
			for (uint i = 0; i < crc_lookup.Length; i++)
			{
				crc_lookup[i] = crc_entry(i);
			}
		}

		private static uint crc_entry(uint index)
		{
			uint r = index << 24;

			for(int i=0; i<8; i++)
			{
				if((r& 0x80000000)!=0)
				{
					r=(r << 1)^0x04c11db7;  // The same as the ethernet generator
											// polynomial, although we use an
											// unreflected alg and an init/final
											// of 0, not 0xffffffff.
				}
				else
				{
					r <<= 1;
				}
			}

			return (r & 0xffffffff);
		}

		/// <summary>
		/// Gets the version of ogg_page used in this page.
		/// </summary>
		/// <returns>the version of ogg_page used in this page.</returns>
		internal int version()
		{
			return header_base[header+4]&0xff;
		}

		/// <summary>
		/// Gets whether this page contains packet data which 
		/// has been continued from the previous page. 
		/// </summary>
		/// <returns>
		/// 0 if this page does not contain continued data.
		/// 1 if this page contains packet data continued from the last page.
		/// </returns>
		internal int continued()
		{
			return (header_base[header+5]&0x01);
		}

		/// <summary>
		/// Gets whether or not this page is at the beginning of the logical bitstream.
		/// </summary>
		/// <returns>
		/// 0 if this page is the beginning of a bitstream.
		/// Greater than zero if the page is from any other location in the stream.
		/// </returns>
		public int bos()
		{
			return (header_base[header+5]&0x02);
		}

		/// <summary>
		/// Gets whether or not this page is at the end of the logical bitstream.
		/// </summary>
		/// <returns>
		/// 0 - This page is from any other location in the stream.
		/// Greater than zero - This page contains the end of a bitstream.
		/// </returns>
		public int eos()
		{
			return (header_base[header+5]&0x04);
		}

		/// <summary>
		/// Gets the exact granular position of the packet data contained at the end of this page.
		/// </summary>
		/// <returns>the exact granular position of the packet data contained at the end of this page.</returns>
		public long granulepos()
		{
			long foo = header_base[header+13]&0xff;

			foo = (foo<<8) | (uint)(header_base[header+12]&0xff);
			foo = (foo<<8) | (uint)(header_base[header+11]&0xff);
			foo = (foo<<8) | (uint)(header_base[header+10]&0xff);
			foo = (foo<<8) | (uint)(header_base[header+9]&0xff);
			foo = (foo<<8) | (uint)(header_base[header+8]&0xff);
			foo = (foo<<8) | (uint)(header_base[header+7]&0xff);
			foo = (foo<<8) | (uint)(header_base[header+6]&0xff);

			return foo;
		}

		/// <summary>
		/// Gets the unique serial number for the logical bitstream of this page. 
		/// Each page contains the serial number for the logical bitstream that it belongs to.
		/// </summary>
		/// <returns>the unique serial number for the logical bitstream of this page.</returns>
		public int serialno()
		{
			return (header_base[header+14]&0xff)      |
				  ((header_base[header+15]&0xff)<<8)  |
				  ((header_base[header+16]&0xff)<<16) |
				  ((header_base[header+17]&0xff)<<24);
		}

		/// <summary>
		/// Gets the sequential page number.
		/// </summary>
		/// <returns>the sequential page number.</returns>
		internal int pageno()
		{
			return (header_base[header+18]&0xff)      |
				  ((header_base[header+19]&0xff)<<8)  |
				  ((header_base[header+20]&0xff)<<16) |
				  ((header_base[header+21]&0xff)<<24);
		}

		/// <summary>
		/// Checksums a page.
		/// </summary>
		internal void checksum()
		{
			uint crc_reg=0;
			uint a, b;

			for(int i=0;i<header_len;i++)
			{
				a = header_base[header+i] & 0xffu;
				b = (crc_reg >> 24) & 0xff;
				crc_reg = (crc_reg<<8)^crc_lookup[a^b];
				//crc_reg = (crc_reg<<8)^(uint)(crc_lookup[((crc_reg >> 24)&0xff)^(header_base[header+i]&0xff)]);
			}

			for(int i=0;i<body_len;i++)
			{
				a = body_base[body+i] & 0xffu;
				b = (crc_reg >> 24) & 0xff;
				crc_reg = (crc_reg<<8)^crc_lookup[a^b];

				//crc_reg = (crc_reg<<8)^(uint)(crc_lookup[((crc_reg >> 24)&0xff)^(body_base[body+i]&0xff)]);
			}

			header_base[header+22]=(byte)crc_reg;
			header_base[header+23]=(byte)(crc_reg>>8);
			header_base[header+24]=(byte)(crc_reg>>16);
			header_base[header+25]=(byte)(crc_reg>>24);
		}
	}
}
