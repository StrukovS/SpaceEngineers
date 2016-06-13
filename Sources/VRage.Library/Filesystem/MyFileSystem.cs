using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using System.Xml;
using System.Xml.Serialization;

using System.Threading;
using System.IO.Compression;

namespace VRage.FileSystem
{
    public class LoadFilesUsageData
    {
        public class FileInfo
        {
            public string name;
            public long size;
            public int refCount;
        }

        public class Data
        {
            public List<FileInfo> files = new List<FileInfo>();
            public long totalBytesInFiles;
            public int duplicateFilesAccess;
            public long duplicateFilesAccessSize;
        }
        
        private Dictionary<string, FileInfo> usedFiles = new Dictionary<string, FileInfo>();
        private Data data = new Data();

        public long BytesProcessed
        {
            get
            {
                return data.totalBytesInFiles + data.duplicateFilesAccessSize;
            }
        }

        public List<FileInfo> Files
        {
            get
            {
                List<FileInfo> dataCopy;
                lock ( mthLock )
                {
                    dataCopy = new List<FileInfo>( data.files );
                }
                return dataCopy;
            }
        }

        private Object mthLock = new Object();

        public void RegisterFile( string fileName, long size )
        {
            lock ( mthLock )
            {
                var lowerFileName = fileName.ToLower();
                FileInfo fileInfo;
                if ( usedFiles.TryGetValue( lowerFileName, out fileInfo ) )
                {
                    ++fileInfo.refCount;
                    ++data.duplicateFilesAccess;
                    data.duplicateFilesAccessSize += fileInfo.size;
                }
                else
                {
                    fileInfo = new FileInfo() { size = size, refCount = 1, name = lowerFileName };
                    data.files.Add( fileInfo );
                    usedFiles[ lowerFileName ] = fileInfo;
                    data.totalBytesInFiles += size;
                }
            }
        }

        public static readonly Encoding DefaultEncoding = new UTF8Encoding( false );
        private static readonly XmlSerializer XmlSerializer = new XmlSerializer( typeof( Data ) );


        public byte [] Encode()
        {
            lock ( mthLock )
            {
                try
                {
                    using ( var memoryStream = new MemoryStream() )
                    {
                        using ( var writeStream = new GZipStream( memoryStream, CompressionMode.Compress, true ) )
                        {
                            XmlWriterSettings settings = new XmlWriterSettings();
                            settings.Encoding = DefaultEncoding;
                            settings.Indent = true;
                            settings.CloseOutput = false;

                            var streamWriter = XmlTextWriter.Create( writeStream, settings );

                            XmlSerializer.Serialize( streamWriter, data );
                            streamWriter.Flush();
                            streamWriter.Close();
                        }
                        byte[] rawData = null;
                        int bytesToRead = ( int ) memoryStream.Length;
                        rawData = new byte[ bytesToRead ];
                        memoryStream.Position = 0;
                        memoryStream.Read( rawData, 0, bytesToRead );

                        return rawData;
                    }
                }
                catch ( Exception /*exc*/ )
                {
                    //  Write exception to log, but continue as if nothing wrong happened
                    //MySandboxGame.Log.WriteLine( "Exception occured, but application is continuing. Exception: " + exc );
                    //MySandboxGame.Log.WriteLine( "LoadFilesUsageData:" );
                }
                return null;
            }
        }

        public void Save( string fileName )
        {
            try
            {
                byte [] data = Encode();
                if ( data == null )
                    return;

                using ( var stream = MyFileSystem.OpenWrite( fileName ) )
                {
                    stream.Write( data, 0, data.Length );
                    stream.Close();
                }
            }
            catch ( Exception /*exc*/ )
            {
                //  Write exception to log, but continue as if nothing wrong happened
                //MySandboxGame.Log.WriteLine( "Exception occured, but application is continuing. Exception: " + exc );
                //MySandboxGame.Log.WriteLine( "LoadFilesUsageData:" );
            }
        }

        public void Clear()
        {
            lock ( mthLock )
            {
                usedFiles.Clear();
                data.files.Clear();
                data.totalBytesInFiles = 0;
            }
        }

        public static LoadFilesUsageData Decode( byte[] compressedData )
        {
            Data serializationData = null;
            try
            {
                using ( var compressedMs = new MemoryStream( compressedData ) )
                {
                    using ( var decompressedMs = new MemoryStream() )
                    {
                        using ( var gzs = new BufferedStream( new GZipStream( compressedMs, CompressionMode.Decompress ), 1024*1024 ) )
                        {
                            gzs.CopyTo( decompressedMs );
                        }

                        var text = DefaultEncoding.GetString( decompressedMs.ToArray() );
                        serializationData = ( Data ) XmlSerializer.Deserialize( new StringReader( text ) );
                    }
                }
            }
            catch ( Exception /*exc*/ )
            {
                //  Write exception to log, but continue as if nothing wrong happened
                //MySandboxGame.Log.WriteLine( "Exception occured, but application is continuing. Exception: " + exc );
                //MySandboxGame.Log.WriteLine( "LoadFilesUsageData:" );
            }

            if ( serializationData == null )
                return null;
            LoadFilesUsageData content = new LoadFilesUsageData();
            content.data = serializationData;
            foreach ( var entry in serializationData.files )
            {
                content.usedFiles[ entry.name ] = entry;
            }
            return content;
        }

        public static LoadFilesUsageData Load( string fileName )
        {
            try
            {
                if ( !File.Exists( fileName ) )
                {
                    //MySandboxGame.Log.WriteLine( "LoadFilesUsageData file not found! " + fileName );
                    return null;
                }
                else
                {
                    using ( var stream = MyFileSystem.OpenRead( fileName ) )
                    {
                        int bytesToRead = ( int ) stream.Length;
                        byte[] rawData = new byte[ bytesToRead ];
                        stream.Read( rawData, 0, bytesToRead );
                        return Decode( rawData );
                    }
                }
            }
            catch ( Exception /*exc*/ )
            {
                //  Write exception to log, but continue as if nothing wrong happened
                //MySandboxGame.Log.WriteLine( "Exception occured, but application is continuing. Exception: " + exc );
                //MySandboxGame.Log.WriteLine( "LoadFilesUsageData:" );
            }
            return null;
        }
    }

    public class MyFileSystemFeeder
    {
        long memoryLimit = 2L * 1024 * 1024 * 1024;
        long usedMemory = 0;


        class LocalFileInfo
        {
            //public MemoryStream memoryStream;
            public byte[] fileContent;
            public string fileName;
            public int refCount;
            public long size;
            public System.DateTime requested;
            public System.DateTime loaded;
            public System.TimeSpan lifeTime;
        }

        Dictionary<string, LocalFileInfo> cachedFiles = new Dictionary<string, LocalFileInfo>();
        List<LocalFileInfo> cachedFilesList = new List<LocalFileInfo>();
        List<LocalFileInfo> pendingRelease = new List<LocalFileInfo>();

        Dictionary<string, LocalFileInfo> cacheFilesRequest = new Dictionary<string, LocalFileInfo>();
        List<LocalFileInfo> cacheFilesRequestList = new List<LocalFileInfo>();

        bool exiting;
        Thread processStreamingThread;
        
        public void Init()
        {
            processStreamingThread = new Thread( ProcessStreamingImpl );
            processStreamingThread.Start();
        }

        public void Done()
        {
            exiting = true;
            processStreamingThread.Join();
        }

        AutoResetEvent syncEvent = new AutoResetEvent( false );

        private Object mthLock = new Object();

        private void ProcessStreamingImplSub()
        {
            while ( true )
            {
                LocalFileInfo localFileInfo = null;
                lock ( mthLock )
                {
                    if ( cacheFilesRequestList.Count == 0 )
                        break;
                    var fileInfo = cacheFilesRequestList[ 0 ];
                    if ( ( fileInfo.size + usedMemory ) >= memoryLimit )
                        break;
                    localFileInfo = fileInfo;
                    cacheFilesRequestList.RemoveAt( 0 );
                    cacheFilesRequest.Remove( localFileInfo.fileName );
                }

                if ( localFileInfo != null )
                {
                    try
                    {
                        using ( var fileStream = MyFileSystem.OpenRead( localFileInfo.fileName ) )
                        {
                            if ( fileStream != null )
                            {
                                int toRead = ( int ) fileStream.Length;
                                localFileInfo.fileContent = new byte[ toRead ];
                                try
                                {
                                    fileStream.Read( localFileInfo.fileContent, 0, toRead );
                                }
                                catch (Exception e )
                                {
                                    localFileInfo.fileContent = null;
                                    throw e;
                                }
                                //localFileInfo.memoryStream = new MemoryStream( ( int ) fileStream.Length );
                                //fileStream.CopyTo( localFileInfo.memoryStream );
                                //localFileInfo.memoryStream.Position = 0;
                                localFileInfo.loaded = DateTime.Now;
                            }
                        }

                        lock ( mthLock )
                        {
                            if ( localFileInfo.fileContent != null )
                            {
                                usedMemory += localFileInfo.fileContent.Length;
                                cachedFiles[ localFileInfo.fileName ] = localFileInfo;
                                cachedFilesList.Add( localFileInfo );
                            }
                        }
                    }
                    catch ( Exception /*exc*/ )
                    {
                        //  Write exception to log, but continue as if nothing wrong happened
                        //MySandboxGame.Log.WriteLine( "Exception occured, but application is continuing. Exception: " + exc );
                        //MySandboxGame.Log.WriteLine( "LoadFilesUsageData:" );
                    }
                }
            }
        }

        void ProcessStreamingImpl()
        {
            while ( !exiting )
            {
                syncEvent.WaitOne( 250 );
                ProcessStreamingImplSub();
                ProcessCacheLifeTime();
                ProcessPendingRelease();
            }
        }

        int lifetimeProcessingIndex;
        void ProcessCacheLifeTime()
        {
            var timeNow = DateTime.Now;
            int iterationsToProcess = 250;
            lock ( mthLock )
            {
                if ( iterationsToProcess > cachedFilesList.Count )
                    iterationsToProcess = cachedFilesList.Count;
                if ( iterationsToProcess == 0 )
                    return;

                for ( ; ;  )
                {
                    if ( --iterationsToProcess <= 0 )
                        break;

                    --lifetimeProcessingIndex;
                    if ( lifetimeProcessingIndex < 0 )
                        lifetimeProcessingIndex = cachedFilesList.Count - 1;
                    if ( lifetimeProcessingIndex >= cachedFilesList.Count )
                        lifetimeProcessingIndex = 0;

                    var localFileInfo = cachedFilesList[ lifetimeProcessingIndex ];
                    if ( timeNow - localFileInfo.loaded > localFileInfo.lifeTime )
                    {
                        cachedFilesList.RemoveAt( lifetimeProcessingIndex );
                        pendingRelease.Add( localFileInfo );
                        cachedFiles.Remove( localFileInfo.fileName );
                    }
                }
            }

        }

        void ProcessPendingRelease()
        {
            lock ( mthLock )
            {
                foreach ( var entry in pendingRelease )
                {
                    if ( entry.fileContent != null )
                    {
                        usedMemory -= entry.fileContent.Length;
                        entry.fileContent = null;
                    }
                }

                pendingRelease.Clear();
            }
        }

        private void DerefCacheFileAndReleaseIfNeccecary( LocalFileInfo localFileInfo, Dictionary<string, LocalFileInfo> filesDict, List<LocalFileInfo> filesList )
        {
            --localFileInfo.refCount;

            if ( localFileInfo.refCount <= 0 )
            {
                filesDict.Remove( localFileInfo.fileName );
                filesList.Remove( localFileInfo ); // slow. may be need to change for hashset, but it will be hard to process lifiteme with portions
                pendingRelease.Add( localFileInfo );
                syncEvent.Set();
            }
        }

        public Stream TryGetFile( string fileName )
        {
            if ( cachedFiles.Count == 0 && cacheFilesRequest.Count == 0 )
                return null;

            var lowerFileName = fileName.ToLower();
            LocalFileInfo localFileInfo = null;
            
            byte[] fileContent = null; // pointer to lock content cause of mth async release
            lock ( mthLock )
            {
                if ( !cachedFiles.TryGetValue( lowerFileName, out localFileInfo ) )
                {
                    // cache fucked up.
                    if ( cacheFilesRequest.TryGetValue( lowerFileName, out localFileInfo ) )
                    {
                        // fuck up case #1
                        // got request, but cache not ready.
                        DerefCacheFileAndReleaseIfNeccecary( localFileInfo, cacheFilesRequest, cacheFilesRequestList );
                    }
                    else
                    {
                        // fuck up case #2
                        // requested unkonwn/uncached file. this is normal case, but loading perfomance hit
                    }
                    return null;
                }

                fileContent = localFileInfo.fileContent;
                DerefCacheFileAndReleaseIfNeccecary( localFileInfo, cachedFiles, cachedFilesList );
            }

            if ( fileContent != null )
            {
                return new MemoryStream( fileContent );
            }

            return null;
        }

        public void FlushCache( LoadFilesUsageData data )
        {
            lock ( mthLock )
            {
                if ( data != null )
                {
                    foreach ( var storedFileInfo in data.Files )
                    {
                        LocalFileInfo localFileInfo;
                        if ( cachedFiles.TryGetValue( storedFileInfo.name, out localFileInfo ) )
                        {
                            cachedFiles.Remove( localFileInfo.fileName );
                            cachedFilesList.Remove( localFileInfo ); // slow. may be need to change for hashset, but it will be hard to process lifiteme with portions
                            pendingRelease.Add( localFileInfo );
                            continue;
                        }
                        if ( cacheFilesRequest.TryGetValue( storedFileInfo.name, out localFileInfo ) )
                        {
                            cacheFilesRequest.Remove( localFileInfo.fileName );
                            cacheFilesRequestList.Remove( localFileInfo ); // slow. may be need to change for hashset, but it will be hard to process lifiteme with portions
                            pendingRelease.Add( localFileInfo );
                            continue;
                        }
                    }
                } else
                {
                    // use array concat?
                    foreach ( var localFileInfo in cachedFiles.Values )
                    {
                        pendingRelease.Add( localFileInfo );
                    }
                    cachedFiles.Clear();
                    cachedFilesList.Clear();
                    foreach ( var localFileInfo in cacheFilesRequest.Values )
                    {
                        pendingRelease.Add( localFileInfo );
                    }
                    cacheFilesRequest.Clear();
                    cacheFilesRequestList.Clear();
                }
            }
        }

        public void Feed( LoadFilesUsageData data, System.TimeSpan lifeTime )
        {
            lock ( mthLock )
            {
                foreach ( var file in data.Files )
                {
                    var localFileInfo = new LocalFileInfo() { fileName = file.name, refCount = file.refCount, size = file.size, requested = DateTime.Now, lifeTime = lifeTime };
                    cacheFilesRequestList.Add( localFileInfo );
                    cacheFilesRequest[ file.name ] = localFileInfo;
                }
            }
        }
    }

    public static class MyFileSystem
    {
#if !UNSHARPER
        public static readonly Assembly MainAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        public static readonly string MainAssemblyName = MainAssembly.GetName().Name;
        public static string ExePath = new FileInfo(MainAssembly.Location).DirectoryName; // OM: Need to be able to alter this due to starting game from tools
#else
		public static string ExePath = @"."; // OM: Need to be able to alter this due to starting game from tools
#endif
        private static string m_contentPath;
        private static string m_modsPath;
        private static string m_userDataPath;
        private static string m_savesPath;

        public static string ContentPath { get { CheckInitialized();  return m_contentPath; } }
        public static string ModsPath { get { CheckInitialized(); return m_modsPath; } }
        public static string UserDataPath { get { CheckInitialized(); return m_userDataPath; } }        
        public static string SavesPath { get { CheckUserSpecificInitialized(); return m_savesPath; } }

        public static IFileVerifier FileVerifier = new MyNullVerifier();
        static MyFileProviderAggregator m_fileProvider = new MyFileProviderAggregator
            (
                new MyClassicFileProvider(),
                new MyZipFileProvider()
            );

        private static void CheckInitialized()
        {
            if (m_contentPath == null)
#if XB1
                MyFileSystem.Init(".", ".");
#else
                throw new InvalidOperationException("Paths are not initialized, call 'Init'");
#endif
        }

        private static void CheckUserSpecificInitialized()
        {
            if (m_userDataPath == null)
                throw new InvalidOperationException("User specific path not initialized, call 'InitUserSpecific'");
        }


        public static void Init(string contentPath, string userData, string modDirName = "Mods")
        {
            FileSystemFeeder.Init();

            if ( m_contentPath != null )
#if XB1
                return;
#else
                throw new InvalidOperationException("Paths already initialized");
#endif

            m_contentPath = contentPath;
            m_userDataPath = userData;
            m_modsPath = Path.Combine(m_userDataPath, modDirName);
            Directory.CreateDirectory(m_modsPath);
        }

        public static void InitUserSpecific(string userSpecificName, string saveDirName = "Saves")
        {
            CheckInitialized();

            if (m_savesPath != null)
                throw new InvalidOperationException("User specific paths already initialized");

            m_savesPath = Path.Combine(m_userDataPath, saveDirName, userSpecificName ?? String.Empty);

            Directory.CreateDirectory(m_savesPath);
        }

        public static void Reset()
        {
            m_contentPath = m_modsPath = m_userDataPath = m_savesPath = null;
        }

        public static void Done()
        {
            FileSystemFeeder.Done();
        }

        static Object loadStatisticLocker = new Object();
        static LoadFilesUsageData loadFilesUsage = null;

        static public LoadFilesUsageData LoadFilesUsage
        {
            get
            {
                return loadFilesUsage;
            }

            set
            {
                lock ( loadStatisticLocker )
                {
                    loadFilesUsage = value;
                }
            }
        }

        public static readonly MyFileSystemFeeder FileSystemFeeder = new MyFileSystemFeeder();


        public interface IMyFileSystemHook
        {
            Stream Open( string path, FileMode mode, FileAccess access, FileShare share );
        }

        static List<IMyFileSystemHook> openHooks = new List<IMyFileSystemHook>();
        
        public static bool InstallHook( IMyFileSystemHook hook )
        {
            lock ( openHooks )
            {
                bool areadyInstalled = openHooks.Contains( hook );
                if ( areadyInstalled )
                {
                    //MyDebug.Assert( !areadyInstalled );
                    return false;
                }

                openHooks.Add( hook );
            }
            return true;
        }
        
        public static bool RemoveHook( IMyFileSystemHook hook )
        {
            bool removed;
            lock ( openHooks )
            {
                removed = openHooks.Remove( hook );
                //MyDebug.Assert( removed );
            }
            return removed;
        }

        public static Stream Open( string path, FileMode mode, FileAccess access, FileShare share )
        {
            // Verifier is enable only when opening files with mode Open and access Read or ReadWrite
            bool verify = ( mode == FileMode.Open ) && ( access != FileAccess.Write );

            Stream stream = null;

            lock ( openHooks )
            {
                foreach ( var hook in openHooks )
                {
                    stream = hook.Open( path, mode, access, share );
                    if ( stream != null )
                        break;
                }
            }

            if ( stream == null && access == FileAccess.Read )
            {
                stream = FileSystemFeeder.TryGetFile( path );
            }

            if ( stream == null )
            {
                stream = m_fileProvider.Open( path, mode, access, share );
                if ( stream != null && access == FileAccess.Read )
                {
                    lock ( loadStatisticLocker )
                    {
                        if ( loadFilesUsage != null )
                        {
                            loadFilesUsage.RegisterFile( path, stream.Length );
                        }
                    }
                }
            }

            return verify && stream != null? FileVerifier.Verify(path, stream) : stream;
        }

        /// <summary>
        /// Opens file for reading
        /// </summary>
        public static Stream OpenRead(string path)
        {
            return Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>
        /// Opens file for reading, convenient method with two paths to combine
        /// </summary>
        public static Stream OpenRead(string path, string subpath)
        {
            return OpenRead(Path.Combine(path, subpath));
        }

        /// <summary>
        /// Creates or overwrites existing file
        /// </summary>
        public static Stream OpenWrite(string path, FileMode mode = FileMode.Create)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return File.Open(path, mode, FileAccess.Write, FileShare.Read);
        }

        /// <summary>
        /// Creates or overwrites existing file, convenient method with two paths to combine
        /// </summary>
        public static Stream OpenWrite(string path, string subpath, FileMode mode = FileMode.Create)
        {
            return OpenWrite(Path.Combine(path, subpath), mode);
        }

        /// <summary>
        /// Checks write access for file
        /// </summary>
        public static bool CheckFileWriteAccess(string path)
        {
            try
            {
                using (var stream = OpenWrite(path, FileMode.Append))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool FileExists(string path)
        {
            return m_fileProvider.FileExists(path);
        }

        public static bool DirectoryExists(string path)
        {
            return m_fileProvider.DirectoryExists(path);
        }

        public static IEnumerable<string> GetFiles(string path)
        {
            return m_fileProvider.GetFiles(path, "*", VRage.FileSystem.MySearchOption.AllDirectories);
        }

        public static IEnumerable<string> GetFiles(string path, string filter)
        {
            return m_fileProvider.GetFiles(path, filter, VRage.FileSystem.MySearchOption.AllDirectories);
        }

        public static IEnumerable<string> GetFiles(string path, string filter, VRage.FileSystem.MySearchOption searchOption)
        {
            return m_fileProvider.GetFiles(path, filter, searchOption);
        }
    }
}
