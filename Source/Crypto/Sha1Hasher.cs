#if NETFX_CORE
using Windows.Security.Cryptography.Core;
#endif

namespace NongCrypto
{
#if NETFX_CORE
    public class Sha1Hasher : CryptoCoreHasher
    {
        public Sha1Hasher()
        {
            provider = HashAlgorithmProvider.OpenAlgorithm (HashAlgorithmNames.Sha1);
            hasher = provider.CreateHash();
        }
    }
#else
    public class Sha1Hasher : CryptoFullHasher
    {
        public Sha1Hasher()
         => hasher = new System.Security.Cryptography.SHA1CryptoServiceProvider();

        public override string Name => "Sha1";
    }
#endif
}
