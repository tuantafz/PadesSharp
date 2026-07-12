using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace PadesSharpDemoApp.Services;

public static class CertificatePickerService
{
    /// <summary>
    /// Mở Windows Certificate Store, lọc chứng thư có private key và còn hạn.
    /// Trả về null nếu người dùng huỷ.
    /// </summary>
    public static X509Certificate2? Pick()
    {
        using var store = new X509Store(StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var eligible = store.Certificates
            .Find(X509FindType.FindByTimeValid, DateTime.Now, validOnly: false)
            .Cast<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .ToArray();

        var col = new X509Certificate2Collection(eligible);

        var selected = X509Certificate2UI.SelectFromCollection(
            col,
            "Chọn chứng thư ký số",
            "Chọn chứng thư có private key để ký PDF:",
            X509SelectionFlag.SingleSelection);

        return selected.Count > 0 ? selected[0] : null;
    }

    /// <summary>Xây dựng chain từ chứng thư đã chọn (không kiểm tra revocation).</summary>
    public static IReadOnlyList<X509Certificate2> BuildChain(X509Certificate2 cert)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        bool built = chain.Build(cert);

        var result = chain.ChainElements
            .Cast<X509ChainElement>()
            .Select(e => e.Certificate)
            .ToList();

        // Nếu chain build thất bại nhưng vẫn có phần tử, dùng những gì có
        if (!built && result.Count == 0)
            result.Add(cert);

        return result;
    }
}
