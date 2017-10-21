#if NETFX_CORE
using Windows.Security.Cryptography.Core;
#endif

namespace NongCrypto
{
#if NETFX_CORE
    public class Sha256Hasher : CryptoCoreHasher
    {
        public Sha256Hasher()
        {
            provider = HashAlgorithmProvider.OpenAlgorithm (HashAlgorithmNames.Sha256);
            hasher = provider.CreateHash();
        }
    }
#else
    public class Sha256Hasher : CryptoFullHasher
    {
        public Sha256Hasher()
        {
            hasher = new System.Security.Cryptography.SHA256CryptoServiceProvider();
        }

        public override string Name { get { return "Sha256"; } }
    }
#endif
}
