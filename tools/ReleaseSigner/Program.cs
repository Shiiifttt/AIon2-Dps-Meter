using System.IO.Compression;
using System.Security.Cryptography;
using AionDpsMeter.Services.Services.Update;

// Usage:
//   keygen <privateKeyPath>                 create a new key pair, print the public key
//   pubkey <privateKeyPath>                 print the public key for an existing private key
//   pack   <folder> <zipPath>               zip a folder (folder name kept as the top-level entry)
//   sign   <privateKeyPath> <zipPath> <tag> write <zipPath>.sig and verify it against UpdateSigning.PublicKey

const string Usage = "Usage: keygen <key> | pubkey <key> | pack <folder> <zip> | sign <key> <zip> <tag>";

if (args.Length == 0)
    return Fail(Usage);

switch (args[0])
{
    case "pack" when args.Length == 3:
    {
        // Windows PowerShell's Compress-Archive can write '\' separators, which the Updater
        // would extract into a subfolder; .NET always writes '/'.
        var (folder, zipPath) = (args[1], args[2]);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        Console.WriteLine($"Packed {zipPath}");
        return 0;
    }

    case "keygen" when args.Length == 2:
    {
        var keyPath = args[1];
        if (File.Exists(keyPath))
            return Fail($"Refusing to overwrite existing key: {keyPath}");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
        File.WriteAllText(keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
        Console.WriteLine($"Private key written to {keyPath}");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    case "pubkey" when args.Length == 2:
    {
        using var ecdsa = LoadKey(args[1]);
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    case "sign" when args.Length == 4:
    {
        var (keyPath, zipPath, tag) = (args[1], args[2], args[3]);
        using var ecdsa = LoadKey(keyPath);

        var signature = ecdsa.SignData(UpdateSigning.BuildMessage(tag, UpdateSigning.HashFile(zipPath)), HashAlgorithmName.SHA256);
        var sigPath = zipPath + UpdateSigning.SignatureExtension;
        File.WriteAllBytes(sigPath, signature);

        // Catches a private key that doesn't match the public key built into the app.
        if (!UpdateSigning.Verify(zipPath, tag, signature))
        {
            File.Delete(sigPath);
            return Fail("Signature does not verify against UpdateSigning.PublicKey - wrong private key?");
        }

        Console.WriteLine($"Signed {Path.GetFileName(zipPath)} as {tag} -> {Path.GetFileName(sigPath)}");
        return 0;
    }

    default:
        return Fail(Usage);
}

static ECDsa LoadKey(string path)
{
    var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(path));
    return ecdsa;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
