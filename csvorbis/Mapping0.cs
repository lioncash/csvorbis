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
	internal class Mapping0 : FuncMapping
	{
		//static int seq=0;
		public override void free_info(Object imap){}
		public override void free_look(Object imap){}

		public override Object look(DspState vd, InfoMode vm, Object m)
		{
			Info vi = vd.vi;
			LookMapping0 looks = new LookMapping0();
			InfoMapping0 info = looks.map = (InfoMapping0) m;
			looks.mode = vm;

			looks.time_look = new Object[info.submaps];
			looks.floor_look = new Object[info.submaps];
			looks.residue_look = new Object[info.submaps];

			looks.time_func = new FuncTime[info.submaps];
			looks.floor_func = new FuncFloor[info.submaps];
			looks.residue_func = new FuncResidue[info.submaps];

			for (int i = 0; i < info.submaps; i++)
			{
				int timenum = info.timesubmap[i];
				int floornum = info.floorsubmap[i];
				int resnum = info.residuesubmap[i];

				looks.time_func[i] = FuncTime.time_P[vi.time_type[timenum]];
				looks.time_look[i] = looks.time_func[i].look(vd, vm, vi.time_param[timenum]);

				looks.floor_func[i] = FuncFloor.floor_P[vi.floor_type[floornum]];
				looks.floor_look[i] = looks.floor_func[i].
					look(vd, vm, vi.floor_param[floornum]);

				looks.residue_func[i] = FuncResidue.residue_P[vi.residue_type[resnum]];
				looks.residue_look[i] = looks.residue_func[i].
					look(vd, vm, vi.residue_param[resnum]);
			}

			if (vi.psys != 0 && vd.analysisp != 0)
			{
				// ??
			}

			looks.ch = vi.Channels;

			return looks;
		}

		public override void pack(Info vi, Object imap, csBuffer opb)
		{
			InfoMapping0 info = (InfoMapping0) imap;

			// Another 'we meant to do it this way' hack...  up to beta 4, we
			// packed 4 binary zeros here to signify one submapping in use.  We
			// now redefine that to mean four bitflags that indicate use of
			// deeper features; bit0:submappings, bit1:coupling,
			// bit2,3:reserved. This is backward compatable with all actual uses
			// of the beta code.

			if (info.submaps > 1)
			{
				opb.write(1, 1);
				opb.write(info.submaps - 1, 4);
			}
			else
			{
				opb.write(0, 1);
			}

			if (info.coupling_steps > 0)
			{
				opb.write(1, 1);
				opb.write(info.coupling_steps - 1, 8);
				for (int i = 0; i < info.coupling_steps; i++)
				{
					opb.write(info.coupling_mag[i], Util.ilog2(vi.Channels));
					opb.write(info.coupling_ang[i], Util.ilog2(vi.Channels));
				}
			}
			else
			{
				opb.write(0, 1);
			}

			opb.write(0, 2); /* 2,3:reserved */

			// We don't write the channel submappings if we only have one...
			if (info.submaps > 1)
			{
				for (int i = 0; i < vi.Channels; i++)
				{
					opb.write(info.chmuxlist[i], 4);
				}
			}

			for (int i = 0; i < info.submaps; i++)
			{
				opb.write(info.timesubmap[i], 8);
				opb.write(info.floorsubmap[i], 8);
				opb.write(info.residuesubmap[i], 8);
			}
		}

		// Also responsible for range checking
		public override object unpack(Info vi, csBuffer opb)
		{
			InfoMapping0 info = new InfoMapping0();

			if (opb.read(1) != 0)
			{
				info.submaps = opb.read(4) + 1;
			}
			else
			{
				info.submaps = 1;
			}

			if (opb.read(1) != 0)
			{
				info.coupling_steps = opb.read(8) + 1;

				for (int i = 0; i < info.coupling_steps; i++)
				{
					int testM = info.coupling_mag[i] = opb.read(Util.ilog2(vi.Channels));
					int testA = info.coupling_ang[i] = opb.read(Util.ilog2(vi.Channels));

					if (testM < 0 ||
						testA < 0 ||
						testM == testA ||
						testM >= vi.Channels ||
						testA >= vi.Channels)
					{
						info.free();
						return null;
					}
				}
			}

			if (opb.read(2) > 0)
			{
				/* 2,3:reserved */
				info.free();
				return null;
			}

			if (info.submaps > 1)
			{
				for (int i = 0; i < vi.Channels; i++)
				{
					info.chmuxlist[i] = opb.read(4);
					if (info.chmuxlist[i] >= info.submaps)
					{
						info.free();
						return null;
					}
				}
			}

			for (int i = 0; i < info.submaps; i++)
			{
				info.timesubmap[i] = opb.read(8);
				if (info.timesubmap[i] >= vi.times)
				{
					info.free();
					return null;
				}

				info.floorsubmap[i] = opb.read(8);
				if (info.floorsubmap[i] >= vi.floors)
				{
					info.free();
					return null;
				}

				info.residuesubmap[i] = opb.read(8);
				if (info.residuesubmap[i] >= vi.residues)
				{
					info.free();
					return null;
				}
			}

			return info;
		}

		private float[][] pcmbundle = null;
		private int[] zerobundle = null;
		private int[] nonzero = null;
		private Object[] floormemo = null;

		public override int inverse(Block vb, Object l)
		{
			lock (this)
			{
				DspState vd = vb.vd;
				Info vi = vd.vi;
				LookMapping0 look = (LookMapping0) l;
				InfoMapping0 info = look.map;
				InfoMode mode = look.mode;
				int n = vb.pcmend = vi.blocksizes[vb.W];

				float[] window = vd.wnd[vb.W][vb.lW][vb.nW][mode.windowtype];
				// float[][] pcmbundle=new float[vi.Channels][];
				// int[] nonzero=new int[vi.Channels];
				if (pcmbundle == null || pcmbundle.Length < vi.Channels)
				{
					pcmbundle = new float[vi.Channels][];
					nonzero = new int[vi.Channels];
					zerobundle = new int[vi.Channels];
					floormemo = new Object[vi.Channels];
				}

				// time domain information decode (note that applying the
				// information would have to happen later; we'll probably add a
				// function entry to the harness for that later
				// NOT IMPLEMENTED

				// recover the spectral envelope; store it in the PCM vector for now 
				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					int submap = info.chmuxlist[i];

					floormemo[i] = look.floor_func[submap].inverse1(vb, look.floor_look[submap], floormemo[i]);
					if (floormemo[i] != null)
					{
						nonzero[i] = 1;
					}
					else
					{
						nonzero[i] = 0;
					}

					for (int j = 0; j < n/2; j++)
					{
						pcm[j] = 0;
					}

					//_analysis_output("ifloor",seq+i,pcm,n/2,0,1);
				}

				for (int i = 0; i < info.coupling_steps; i++)
				{
					if (nonzero[info.coupling_mag[i]] != 0 ||
						nonzero[info.coupling_ang[i]] != 0)
					{
						nonzero[info.coupling_mag[i]] = 1;
						nonzero[info.coupling_ang[i]] = 1;
					}
				}

				// recover the residue, apply directly to the spectral envelope
				for (int i = 0; i < info.submaps; i++)
				{
					int ch_in_bundle = 0;
					for (int j = 0; j < vi.Channels; j++)
					{
						if (info.chmuxlist[j] == i)
						{
							if (nonzero[j] != 0)
							{
								zerobundle[ch_in_bundle] = 1;
							}
							else
							{
								zerobundle[ch_in_bundle] = 0;
							}
							pcmbundle[ch_in_bundle++] = vb.pcm[j];
						}
					}

					look.residue_func[i].inverse(vb, look.residue_look[i],
						pcmbundle, zerobundle, ch_in_bundle);
				}

				// Channel coupling
				for (int i = info.coupling_steps - 1; i >= 0; i--)
				{
					float[] pcmM = vb.pcm[info.coupling_mag[i]];
					float[] pcmA = vb.pcm[info.coupling_ang[i]];

					for (int j = 0; j < n/2; j++)
					{
						float mag = pcmM[j];
						float ang = pcmA[j];

						if (mag > 0)
						{
							if (ang > 0)
							{
								pcmM[j] = mag;
								pcmA[j] = mag - ang;
							}
							else
							{
								pcmA[j] = mag;
								pcmM[j] = mag + ang;
							}
						}
						else
						{
							if (ang > 0)
							{
								pcmM[j] = mag;
								pcmA[j] = mag + ang;
							}
							else
							{
								pcmA[j] = mag;
								pcmM[j] = mag - ang;
							}
						}
					}
				}

				// Compute and apply spectral envelope
				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					int submap = info.chmuxlist[i];
					look.floor_func[submap].inverse2(vb, look.floor_look[submap], floormemo[i], pcm);
				}

				// Transform the PCM data; takes PCM vector, vb; modifies PCM vector
				// only MDCT right now....
				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					//_analysis_output("out",seq+i,pcm,n/2,0,0);
					((Mdct) vd.transform[vb.W][0]).backward(pcm, pcm);
				}

				// Now apply the decoded pre-window time information
				// NOT IMPLEMENTED

				// window the data
				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					if (nonzero[i] != 0)
					{
						for (int j = 0; j < n; j++)
						{
							pcm[j] *= window[j];
						}
					}
					else
					{
						for (int j = 0; j < n; j++)
						{
							pcm[j] = 0.0f;
						}
					}
					//_analysis_output("final",seq++,pcm,n,0,0);
				}

				// now apply the decoded post-window time information
				// NOT IMPLEMENTED
				// all done!
				return 0;
			}
		}
	}

	internal class InfoMapping0
	{
		internal int submaps; // <= 16
		internal int[] chmuxlist = new int[256]; // up to 256 channels in a Vorbis stream

		internal int[] timesubmap = new int[16]; // [mux]
		internal int[] floorsubmap = new int[16]; // [mux] submap to floors
		internal int[] residuesubmap = new int[16]; // [mux] submap to residue
		internal int[] psysubmap = new int[16]; // [mux]; encode only

		internal int coupling_steps;
		internal int[] coupling_mag = new int[256];
		internal int[] coupling_ang = new int[256];

		internal void free()
		{
			chmuxlist = null;
			timesubmap = null;
			floorsubmap = null;
			residuesubmap = null;
			psysubmap = null;

			coupling_mag = null;
			coupling_ang = null;
		}
	}

	internal class LookMapping0
	{
		internal InfoMode mode;
		internal InfoMapping0 map;
		internal Object[] time_look;
		internal Object[] floor_look;
		//Object[] floor_state;
		internal Object[] residue_look;
		//PsyLook[] psy_look;

		internal FuncTime[] time_func;
		internal FuncFloor[] floor_func;
		internal FuncResidue[] residue_func;

		internal int ch;
		//float[][] decay;
		//int lastframe; // if a different mode is called, we need to 
		// invalidate decay and floor state
	}
}