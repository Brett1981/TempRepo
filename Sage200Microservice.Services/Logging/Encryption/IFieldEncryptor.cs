namespace Sage200Microservice.Services.Logging.Encryption
{
    public interface IFieldEncryptor
    {
        // Returns a compact string: base64(nonce) + ":" + base64(ciphertext) + ":" + base64(tag)
        string EncryptToToken(string plaintext);
        string DecryptFromToken(string token);
        bool MightBeToken(string s);
    }
}
