// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-172 (issue 439): a generated offline HTML report whose second line holds several inline PNG diagrams, each a data: URI, between
/// captions, as the report in the issue did. Every diagram is synthetic, a PNG signature and header followed by bytes derived from its
/// number, and a credential-shaped value assembled at run time may be written into the encoded data of chosen diagrams.
/// </summary>
public sealed class InlineDataReport
{
    private const string Alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private InlineDataReport(string text, string line, string[] data, IReadOnlyDictionary<int, (string Value, int Column)> credentials)
    {
        Text = text;
        Line = line;
        Data = data;
        Credentials = credentials;
    }

    /// <summary>The report's whole text.</summary>
    public string Text { get; }

    /// <summary>The long line, the report's second, that holds the diagrams.</summary>
    public string Line { get; }

    /// <summary>The encoded data of each diagram, in the line's order.</summary>
    public IReadOnlyList<string> Data { get; }

    /// <summary>Each credential-shaped value written, by the number of the diagram it is in, counted from 1, with its first column on the line.</summary>
    public IReadOnlyDictionary<int, (string Value, int Column)> Credentials { get; }

    /// <summary>The report's line number of the diagrams.</summary>
    public const int LineNumber = 2;

    /// <summary>Words of the captions and alternative text around the diagrams, none of which a finding may carry.</summary>
    public static readonly string[] Surrounding = ["Throughput by stage", "Pipeline diagram", "Figure", "Weekly pipeline report"];

    /// <summary>
    /// A report of <paramref name="diagrams"/> diagrams on one line, with a credential-shaped value, a Google API key's shape that the estate
    /// check knows, in the encoded data of each diagram numbered in <paramref name="credentialIn"/>.
    /// </summary>
    public static InlineDataReport Generate(int diagrams, params int[] credentialIn)
    {
        var data = new string[diagrams];
        var credentials = new Dictionary<int, (string Value, int Column)>();
        var line = new StringBuilder("<main><h1>Weekly pipeline report</h1>");
        for (int i = 1; i <= diagrams; i++)
        {
            byte[] image = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                .. Enumerable.Range(0, 64).SelectMany(block => SHA256.HashData(Encoding.UTF8.GetBytes($"diagram {i} block {block}")))];
            // Letters and digits only, as the encoded data around it is, so that nothing but the value is credential-shaped.
            string encoded = Convert.ToBase64String(image).Replace('+', 'p').Replace('/', 's');
            string? credential = null;
            if (credentialIn.Contains(i))
            {
                credential = "AIza" + RandomNumberGenerator.GetString(Alphanumeric, 35);
                encoded = encoded[..600] + credential + encoded[600..];
            }

            data[i - 1] = encoded;
            line.Append("<figure><img alt=\"Pipeline diagram ").Append(i).Append("\" src=\"data:image/png;base64,");
            if (credential is not null) credentials[i] = (credential, line.Length + 600 + 1);
            line.Append(encoded).Append("\"><figcaption>Figure ").Append(i).Append(": Throughput by stage ").Append(i).Append("</figcaption></figure>");
        }

        line.Append("</main>");
        return new("<!doctype html><title>Weekly pipeline report</title>\n" + line + "\n", line.ToString(), data, credentials);
    }
}
