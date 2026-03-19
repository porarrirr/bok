using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Permissions;
using NAudio.Wave.SampleProviders;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]
[assembly: AssemblyCompany("Mark Heath & Contributors")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCopyright("© Mark Heath 2023")]
[assembly: AssemblyDescription("NAudio, an audio library for .NET")]
[assembly: AssemblyFileVersion("2.2.1.0")]
[assembly: AssemblyInformationalVersion("2.2.1")]
[assembly: AssemblyProduct("NAudio")]
[assembly: AssemblyTitle("NAudio")]
[assembly: AssemblyMetadata("RepositoryUrl", "https://github.com/naudio/NAudio")]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
[assembly: AssemblyVersion("2.2.1.0")]
[module: UnverifiableCode]
namespace NAudio.Wave;

/// <summary>
/// AudioFileReader simplifies opening an audio file in NAudio
/// Simply pass in the filename, and it will attempt to open the
/// file and set up a conversion path that turns into PCM IEEE float.
/// ACM codecs will be used for conversion.
/// It provides a volume property and implements both WaveStream and
/// ISampleProvider, making it possibly the only stage in your audio
/// pipeline necessary for simple playback scenarios
/// </summary>
public class AudioFileReader : WaveStream, ISampleProvider
{
	private WaveStream readerStream;

	private readonly SampleChannel sampleChannel;

	private readonly int destBytesPerSample;

	private readonly int sourceBytesPerSample;

	private readonly long length;

	private readonly object lockObject;

	/// <summary>
	/// File Name
	/// </summary>
	public string FileName { get; }

	/// <summary>
	/// WaveFormat of this stream
	/// </summary>
	public override WaveFormat WaveFormat => sampleChannel.WaveFormat;

	/// <summary>
	/// Length of this stream (in bytes)
	/// </summary>
	public override long Length => length;

	/// <summary>
	/// Position of this stream (in bytes)
	/// </summary>
	public override long Position
	{
		get
		{
			return SourceToDest(((Stream)(object)readerStream).Position);
		}
		set
		{
			lock (lockObject)
			{
				((Stream)(object)readerStream).Position = DestToSource(value);
			}
		}
	}

	/// <summary>
	/// Gets or Sets the Volume of this AudioFileReader. 1.0f is full volume
	/// </summary>
	public float Volume
	{
		get
		{
			return sampleChannel.Volume;
		}
		set
		{
			sampleChannel.Volume = value;
		}
	}

	/// <summary>
	/// Initializes a new instance of AudioFileReader
	/// </summary>
	/// <param name="fileName">The file to open</param>
	public AudioFileReader(string fileName)
	{
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		lockObject = new object();
		FileName = fileName;
		CreateReaderStream(fileName);
		sourceBytesPerSample = readerStream.WaveFormat.BitsPerSample / 8 * readerStream.WaveFormat.Channels;
		sampleChannel = new SampleChannel((IWaveProvider)(object)readerStream, false);
		destBytesPerSample = 4 * sampleChannel.WaveFormat.Channels;
		length = SourceToDest(((Stream)(object)readerStream).Length);
	}

	/// <summary>
	/// Creates the reader stream, supporting all filetypes in the core NAudio library,
	/// and ensuring we are in PCM format
	/// </summary>
	/// <param name="fileName">File Name</param>
	private void CreateReaderStream(string fileName)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Invalid comparison between Unknown and I4
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Invalid comparison between Unknown and I4
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected O, but got Unknown
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Expected O, but got Unknown
		if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
		{
			readerStream = (WaveStream)new WaveFileReader(fileName);
			if ((int)readerStream.WaveFormat.Encoding != 1 && (int)readerStream.WaveFormat.Encoding != 3)
			{
				readerStream = WaveFormatConversionStream.CreatePcmStream(readerStream);
				readerStream = (WaveStream)new BlockAlignReductionStream(readerStream);
			}
		}
		else if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
		{
			if (Environment.OSVersion.Version.Major < 6)
			{
				readerStream = (WaveStream)(object)new Mp3FileReader(fileName);
			}
			else
			{
				readerStream = (WaveStream)new MediaFoundationReader(fileName);
			}
		}
		else if (fileName.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
		{
			readerStream = (WaveStream)new AiffFileReader(fileName);
		}
		else
		{
			readerStream = (WaveStream)new MediaFoundationReader(fileName);
		}
	}

	/// <summary>
	/// Reads from this wave stream
	/// </summary>
	/// <param name="buffer">Audio buffer</param>
	/// <param name="offset">Offset into buffer</param>
	/// <param name="count">Number of bytes required</param>
	/// <returns>Number of bytes read</returns>
	public override int Read(byte[] buffer, int offset, int count)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected O, but got Unknown
		WaveBuffer val = new WaveBuffer(buffer);
		int count2 = count / 4;
		return Read(val.FloatBuffer, offset / 4, count2) * 4;
	}

	/// <summary>
	/// Reads audio from this sample provider
	/// </summary>
	/// <param name="buffer">Sample buffer</param>
	/// <param name="offset">Offset into sample buffer</param>
	/// <param name="count">Number of samples required</param>
	/// <returns>Number of samples read</returns>
	public int Read(float[] buffer, int offset, int count)
	{
		lock (lockObject)
		{
			return sampleChannel.Read(buffer, offset, count);
		}
	}

	/// <summary>
	/// Helper to convert source to dest bytes
	/// </summary>
	private long SourceToDest(long sourceBytes)
	{
		return destBytesPerSample * (sourceBytes / sourceBytesPerSample);
	}

	/// <summary>
	/// Helper to convert dest to source bytes
	/// </summary>
	private long DestToSource(long destBytes)
	{
		return sourceBytesPerSample * (destBytes / destBytesPerSample);
	}

	/// <summary>
	/// Disposes this AudioFileReader
	/// </summary>
	/// <param name="disposing">True if called from Dispose</param>
	protected override void Dispose(bool disposing)
	{
		if (disposing && readerStream != null)
		{
			((Stream)(object)readerStream).Dispose();
			readerStream = null;
		}
		((Stream)this).Dispose(disposing);
	}
}
/// <summary>
/// Class for reading from MP3 files
/// </summary>
public class Mp3FileReader : Mp3FileReaderBase
{
	/// <summary>Supports opening a MP3 file</summary>
	public Mp3FileReader(string mp3FileName)
		: base((Stream)File.OpenRead(mp3FileName), new FrameDecompressorBuilder(CreateAcmFrameDecompressor), true)
	{
	}//IL_000e: Unknown result type (might be due to invalid IL or missing references)
	//IL_0019: Expected O, but got Unknown


	/// <summary>
	/// Opens MP3 from a stream rather than a file
	/// Will not dispose of this stream itself
	/// </summary>
	/// <param name="inputStream">The incoming stream containing MP3 data</param>
	public Mp3FileReader(Stream inputStream)
		: base(inputStream, new FrameDecompressorBuilder(CreateAcmFrameDecompressor), false)
	{
	}//IL_0009: Unknown result type (might be due to invalid IL or missing references)
	//IL_0014: Expected O, but got Unknown


	/// <summary>
	/// Creates an ACM MP3 Frame decompressor. This is the default with NAudio
	/// </summary>
	/// <param name="mp3Format">A WaveFormat object based </param>
	/// <returns></returns>
	public static IMp3FrameDecompressor CreateAcmFrameDecompressor(WaveFormat mp3Format)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected O, but got Unknown
		return (IMp3FrameDecompressor)new AcmMp3FrameDecompressor(mp3Format);
	}
}
