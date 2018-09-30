#if NETFX_CORE
using Windows.Security.Cryptography.Core;
#else
using System.Security.Cryptography;
#endif

namespace NongCrypto
{
#if NETFX_CORE
    public class Md5Hasher : CryptoCoreHasher
    {
        public Md5Hasher()
        {
            provider = HashAlgorithmProvider.OpenAlgorithm (HashAlgorithmNames.Md5);
            hasher = provider.CreateHash();
        }
    }
#else
    public class Md5Hasher : CryptoFullHasher
    {
        public Md5Hasher()
         => hasher = new MD5CryptoServiceProvider();

        public override string Name => "Md5";
    }
#endif
}
