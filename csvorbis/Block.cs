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

using csogg;

namespace csvorbis 
{
	public class Block
	{
		// Necessary stream state for linking to the framing abstraction
		internal float[][] pcm = new float[0][]; // This is a pointer into local storage
		internal csBuffer opb = new csBuffer();

		internal int lW;
		internal int W;
		internal int nW;
		internal int pcmend;
		internal int mode;

		internal int eofflag;
		internal long granulepos;
		internal long sequence;
		internal DspState vd; // For read-only access of configuration

		// Bitmetrics for the frame
		internal int glue_bits = 0;
		internal int time_bits = 0;
		internal int floor_bits = 0;
		internal int res_bits = 0;

		public Block(DspState vd)
		{
			this.vd = vd;
			//  localalloc=0;
			//  localstore=null;
			if (vd.analysisp != 0)
			{
				opb.writeinit();
			}
		}

		public void init(DspState vd)
		{
			this.vd = vd;
		}

		public int clear()
		{
			if (vd != null)
			{
				if (vd.analysisp != 0)
				{
					opb.writeclear();
				}
			}

			return 0;
		}

		public int synthesis(Packet op)
		{
			Info vi = vd.vi;

			// First things first.  Make sure decode is ready
			// ripcord();
			opb.readinit(op.packet_base, op.packet, op.bytes);

			// Check the packet type
			if (opb.read(1) != 0)
			{
				// Oops.  This is not an audio data packet
				return -1;
			}

			// Read our mode and pre/post windowsize
			int _mode = opb.read(vd.modebits);
			if (_mode == -1)
				return -1;

			mode = _mode;
			W = vi.mode_param[mode].blockflag;
			if (W != 0)
			{
				lW = opb.read(1);
				nW = opb.read(1);

				if (nW == -1)
					return -1;
			}
			else
			{
				lW = 0;
				nW = 0;
			}

			// More setup
			granulepos = op.granulepos;
			sequence = op.packetno - 3; // First block is third packet
			eofflag = op.e_o_s;

			// Alloc PCM passback storage
			pcmend = vi.blocksizes[W];
			if (pcm.Length < vi.channels)
			{
				pcm = new float[vi.channels][];
			}
			for (int i = 0; i < vi.channels; i++)
			{
				if (pcm[i] == null || pcm[i].Length < pcmend)
				{
					pcm[i] = new float[pcmend];
				}
				else
				{
					for (int j = 0; j < pcmend; j++)
					{
						pcm[i][j] = 0;
					}
				}
			}

			// Unpack_header enforces range checking
			int type = vi.map_type[vi.mode_param[mode].mapping];
			return (FuncMapping.mapping_P[type].inverse(this, vd.mode[mode]));
		}
	}
}
