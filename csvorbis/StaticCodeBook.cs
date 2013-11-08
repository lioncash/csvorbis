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
using csogg;

namespace csvorbis 
{
	internal class StaticCodeBook
	{
		internal int   dim;            // codebook dimensions (elements per vector)
		internal int   entries;        // codebook entries
		internal int[] lengthlist;     // codeword lengths in bits

		// mapping
		internal int   maptype;        // 0=none
		// 1=implicitly populated values from map column 
		// 2=listed arbitrary values

		// The below does a linear, single monotonic sequence mapping.
		internal int   q_min;       // packed 32 bit float; quant value 0 maps to minval
		internal int   q_delta;     // packed 32 bit float; val 1 - val 0 == delta
		internal int   q_quant;     // bits: 0 < quant <= 16
		internal int   q_sequencep; // bitflag

		// additional information for log (dB) mapping; the linear mapping
		// is assumed to actually be values in dB.  encodebias is used to
		// assign an error weight to 0 dB. We have two additional flags:
		// zeroflag indicates if entry zero is to represent -Inf dB; negflag
		// indicates if we're to represent negative linear values in a
		// mirror of the positive mapping.

		internal int[] quantlist;  // map == 1: (int)(entries/dim) element column map
		// map == 2: list of dim*entries quantized entry vals

		// encode helpers
		internal EncodeAuxNearestMatch nearest_tree = null;
		internal EncodeAuxThreshMatch  thresh_tree = null;

		internal int pack(csBuffer opb)
		{
			bool ordered = false;

			opb.write(0x564342, 24);
			opb.write(dim, 16);
			opb.write(entries, 24);

			// pack the codewords.  There are two packings; length ordered and
			// length random.  Decide between the two now.
			int i;
			for (i = 1; i < entries; i++)
			{
				if (lengthlist[i] < lengthlist[i - 1])
					break;
			}
			if (i == entries)
				ordered = true;

			if (ordered)
			{
				// length ordered.  We only need to say how many codewords of
				// each length.  The actual codewords are generated
				// deterministically

				int count = 0;
				opb.write(1, 1); // ordered
				opb.write(lengthlist[0] - 1, 5); // 1 to 32

				for (i = 1; i < entries; i++)
				{
					int _this = lengthlist[i];
					int _last = lengthlist[i - 1];
					if (_this > _last)
					{
						for (int j = _last; j < _this; j++)
						{
							opb.write(i - count, Util.ilog(entries - count));
							count = i;
						}
					}
				}
				opb.write(i - count, Util.ilog(entries - count));
			}
			else
			{
				// length random.  Again, we don't code the codeword itself, just
				// the length.  This time, though, we have to encode each length
				opb.write(0, 1); // unordered

				// algortihmic mapping has use for 'unused entries', which we tag
				// here.  The algorithmic mapping happens as usual, but the unused
				// entry has no codeword.
				for (i = 0; i < entries; i++)
				{
					if (lengthlist[i] == 0)
						break;
				}

				if (i == entries)
				{
					opb.write(0, 1); // no unused entries
					for (i = 0; i < entries; i++)
					{
						opb.write(lengthlist[i] - 1, 5);
					}
				}
				else
				{
					opb.write(1, 1); // we have unused entries; thus we tag
					for (i = 0; i < entries; i++)
					{
						if (lengthlist[i] == 0)
						{
							opb.write(0, 1);
						}
						else
						{
							opb.write(1, 1);
							opb.write(lengthlist[i] - 1, 5);
						}
					}
				}
			}

			// is the entry number the desired return value, or do we have a
			// mapping? If we have a mapping, what type?
			opb.write(maptype, 4);
			switch (maptype)
			{
				case 0:
					// no mapping
					break;
				case 1:
				case 2:
					// implicitly populated value mapping
					// explicitly populated value mapping
					if (quantlist == null)
					{
						// no quantlist?  error
						return -1;
					}

					// values that define the dequantization
					opb.write(q_min, 32);
					opb.write(q_delta, 32);
					opb.write(q_quant - 1, 4);
					opb.write(q_sequencep, 1);

				{
					int quantvals = 0;
					switch (maptype)
					{
						case 1:
							// a single column of (c->entries/c->dim) quantized values for
							// building a full value list algorithmically (square lattice)
							quantvals = maptype1_quantvals();
							break;
						case 2:
							// every value (c->entries*c->dim total) specified explicitly
							quantvals = entries*dim;
							break;
					}

					// quantized values
					for (i = 0; i < quantvals; i++)
					{
						opb.write(Math.Abs(quantlist[i]), q_quant);
					}
				}
					break;
				default:
					// error case; we don't have any other map types now
					return -1;
			}
			return 0;
		}

		// unpacks a codebook from the packet buffer into the codebook struct,
		// readies the codebook auxiliary structures for decode
		internal int unpack(csBuffer opb)
		{
			// make sure alignment is correct
			if (opb.read(24) != 0x564342)
			{
				clear();
				return -1;
			}

			// first the basic parameters
			dim = opb.read(16);
			entries = opb.read(24);
			if (entries == -1)
			{
				clear();
				return -1;
			}

			// codeword ordering.... length ordered or unordered?
			switch (opb.read(1))
			{
				case 0:
					// unordered
					lengthlist = new int[entries];

					// allocated but unused entries?
					if (opb.read(1) != 0)
					{
						// yes, unused entries

						for (int i = 0; i < entries; i++)
						{
							if (opb.read(1) != 0)
							{
								int num = opb.read(5);
								if (num == -1)
								{
									clear();
									return -1;
								}
								lengthlist[i] = num + 1;
							}
							else
							{
								lengthlist[i] = 0;
							}
						}
					}
					else
					{
						// all entries used; no tagging
						for (int i = 0; i < entries; i++)
						{
							int num = opb.read(5);
							if (num == -1)
							{
								clear();
								return GetHashCode() - 1;
							}
							lengthlist[i] = num + 1;
						}
					}
					break;
				case 1:
					// ordered
				{
					int length = opb.read(5) + 1;
					lengthlist = new int[entries];

					for (int i = 0; i < entries;)
					{
						int num = opb.read(Util.ilog(entries - i));
						if (num == -1)
						{
							clear();
							return -1;
						}
						for (int j = 0; j < num; j++,i++)
						{
							lengthlist[i] = length;
						}
						length++;
					}
				}
					break;
				default:
					// EOF
					return -1;
			}

			// Do we have a mapping to unpack?
			switch ((maptype = opb.read(4)))
			{
				case 0:
					// no mapping
					break;
				case 1:
				case 2:
					// implicitly populated value mapping
					// explicitly populated value mapping
					q_min = opb.read(32);
					q_delta = opb.read(32);
					q_quant = opb.read(4) + 1;
					q_sequencep = opb.read(1);

				{
					int quantvals = 0;
					switch (maptype)
					{
						case 1:
							quantvals = maptype1_quantvals();
							break;
						case 2:
							quantvals = entries*dim;
							break;
					}

					// quantized values
					quantlist = new int[quantvals];
					for (int i = 0; i < quantvals; i++)
					{
						quantlist[i] = opb.read(q_quant);
					}
					if (quantlist[quantvals - 1] == -1)
					{
						clear();
						return -1;
					}
				}
					break;
				default:
					clear();
					return -1;
			}

			// all set
			return 0;
		}

		// there might be a straightforward one-line way to do the below
		// that's portable and totally safe against roundoff, but I haven't
		// thought of it.  Therefore, we opt on the side of caution
		internal int maptype1_quantvals()
		{
			int vals = (int) (Math.Floor(Math.Pow(entries, 1.0/dim)));

			// the above *should* be reliable, but we'll not assume that FP is
			// ever reliable when bitstream sync is at stake; verify via integer
			// means that vals really is the greatest value of dim for which
			// vals^b->bim <= b->entries
			// treat the above as an initial guess
			while (true)
			{
				int acc = 1;
				int acc1 = 1;
				for (int i = 0; i < dim; i++)
				{
					acc *= vals;
					acc1 *= vals + 1;
				}

				if (acc <= entries && acc1 > entries)
				{
					return vals;
				}
				else
				{
					if (acc > entries)
						vals--;
					else
						vals++;
				}
			}
		}

		internal void clear()
		{
		}

		// unpack the quantized list of values for encode/decode
		// we need to deal with two map types: in map type 1, the values are
		// generated algorithmically (each column of the vector counts through
		// the values in the quant vector). in map type 2, all the values came
		// in in an explicit list.  Both value lists must be unpacked
		internal float[] unquantize()
		{
			if (maptype == 1 || maptype == 2)
			{
				float mindel = float32_unpack(q_min);
				float delta = float32_unpack(q_delta);
				float[] r = new float[entries*dim];

				// maptype 1 and 2 both use a quantized value vector, but
				// different sizes
				switch (maptype)
				{
					case 1:
						// most of the time, entries%dimensions == 0, but we need to be
						// well defined.  We define that the possible vales at each
						// scalar is values == entries/dim.  If entries%dim != 0, we'll
						// have 'too few' values (values*dim<entries), which means that
						// we'll have 'left over' entries; left over entries use zeroed
						// values (and are wasted).  So don't generate codebooks like that
						int quantvals = maptype1_quantvals();
						for (int j = 0; j < entries; j++)
						{
							float last = 0.0f;
							int indexdiv = 1;
							for (int k = 0; k < dim; k++)
							{
								int index = (j/indexdiv)%quantvals;
								float val = quantlist[index];
								val = Math.Abs(val)*delta + mindel + last;

								if (q_sequencep != 0)
									last = val;

								r[j*dim + k] = val;
								indexdiv *= quantvals;
							}
						}
						break;

					case 2:
						for (int j = 0; j < entries; j++)
						{
							float last = 0.0f;
							for (int k = 0; k < dim; k++)
							{
								float val = quantlist[j*dim + k];
								val = Math.Abs(val)*delta + mindel + last;

								if (q_sequencep != 0)
									last = val;

								r[j*dim + k] = val;
							}
						}
						break;
				}

				return r;
			}

			return null;
		}

		// 32 bit float (not IEEE; nonnormalized mantissa +
		// biased exponent) : neeeeeee eeemmmmm mmmmmmmm mmmmmmmm 
		// Why not IEEE?  It's just not that important here.

		//private const int VQ_FEXP=10;
		private const int VQ_FMAN = 21;
		private const int VQ_FEXP_BIAS = 768; // bias toward values smaller than 1.

		// doesn't currently guard under/overflow 
		internal static long float32_pack(float val)
		{
			uint sign = 0;

			if (val < 0)
			{
				sign = 0x80000000;
				val = -val;
			}

			int exp = (int) Math.Floor(Math.Log(val)/Math.Log(2)+0.001); // +0.001 = epsilon.
			int mant = (int) Math.Round(ldexp(exp, (VQ_FMAN - 1) - exp));
			exp = (exp + VQ_FEXP_BIAS) << VQ_FMAN;

			return ((int) sign | exp | mant);
		}

		internal static float float32_unpack(int val)
		{
			float mant = val & 0x1fffff;
			float exp = (uint) (val & 0x7fe00000) >> VQ_FMAN;

			if ((val & 0x80000000) != 0)
				mant = -mant;

			return (ldexp(mant, ((int) exp) - (VQ_FMAN - 1) - VQ_FEXP_BIAS));
		}

		internal static float ldexp(float foo, int e)
		{
			return (float) (foo*Math.Pow(2, e));
		}
	}
}
