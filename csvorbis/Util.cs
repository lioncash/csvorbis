namespace csvorbis
{
	/// <summary>
	/// Utility methods
	/// </summary>
	internal static class Util
	{
		internal static int ilog(int v)
		{
			int ret = 0;

			while (v != 0)
			{
				ret++;
				v = (int)((uint)v >> 1);
			}

			return ret;
		}

		internal static int icount(int v)
		{
			int ret = 0;

			while (v != 0)
			{
				ret += (v & 1);
				v = (int)((uint)v >> 1);
			}

			return ret;
		}

		internal static int ilog2(int v)
		{
			int ret = 0;

			while (v > 1)
			{
				ret++;
				v = (int)((uint)v >> 1);
			}

			return ret;
		}
	}
}
