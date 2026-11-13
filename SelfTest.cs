using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Basma
{
    /// <summary>Self-checks on throw-away data in %TEMP%. Never touches real cases. Never writes to any disk device.</summary>
    public static class SelfTest
    {
        public static List<(string name, bool ok, string detail)> Run(string logPath)
        {
            var res = new List<(string, bool, string)>();
            var root = Path.Combine(Path.GetTempPath(), "basma_selftest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            void T(string name, Func<bool> f, Func<string> detail = null)
            {
                try { bool ok = f(); res.Add((name, ok, ok ? "" : detail?.Invoke() ?? "false")); }
                catch (Exception e) { res.Add((name, false, e.GetType().Name + ": " + e.Message)); }
            }
            string W(string rel, byte[] data) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); return p; }
            var rnd = new Random(7);
            byte[] R(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }
            try
            {
                var abc = Engine.HashText("abc", Algos.All);
                T("vector md5(abc)", () => abc.Get("md5") == "900150983cd24fb0d6963f7d28e17f72");
                T("vector sha1(abc)", () => abc.Get("sha1") == "a9993e364706816aba3e25717850c26c9cd0d89d");
                T("vector sha256(abc)", () => abc.Get("sha256") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
                T("vector sha384(abc)", () => abc.Get("sha384") == "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7");
                T("vector sha512(abc)", () => abc.Get("sha512").StartsWith("ddaf35a193617aba"));
                if (Algos.Supported("sha3_256")) T("vector sha3-256(abc)", () => abc.Get("sha3_256") == "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532");
                T("vector crc32(123456789)", () => Engine.HashText("123456789", new[] { "crc32" }).Get("crc32") == "cbf43926");

                var big = R(Engine.Chunk * 2 + 12345);
                var bigP = W("big.bin", big);
                var hb = Engine.HashFile(bigP, Algos.All, new Prog());
                T("multi-chunk file = one-shot (sha256, md5, sha1)", () => hb.Get("sha256") == Fmt.Hex(SHA256.HashData(big)) && hb.Get("md5") == Fmt.Hex(MD5.HashData(big)) && hb.Get("sha1") == Fmt.Hex(SHA1.HashData(big)) && hb.Size == big.Length);

                var a = R(1000); var b = R(2000); var c = R(5);
                W("img/disk.001", a); W("img/disk.002", b); W("img/disk.003", c);
                var hs = Engine.HashFile(Path.Combine(root, "img/disk.001"), new[] { "sha256" }, new Prog(), true);
                T("split image hashed as one stream", () => hs.TargetKind == "split-image" && hs.FileCount == 3 && hs.Get("sha256") == Fmt.Hex(SHA256.HashData(a.Concat(b).Concat(c).ToArray())));

                var f = Path.Combine(root, "f");
                W("f/a.txt", Encoding.UTF8.GetBytes("hello"));
                W("f/sub/photo.jpg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[100]).ToArray());
                W("f/sub/fake.pdf", Encoding.UTF8.GetBytes("just text"));
                W("f/sub/real.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[100]).ToArray());
                W("f/vault.bin", R(300000));
                W("f/copy.txt", Encoding.UTF8.GetBytes("hello"));
                var kd = Path.Combine(root, "sets"); var ks = new KnownSets(kd);
                var badList = W("bad.txt", Encoding.UTF8.GetBytes(Fmt.Hex(MD5.HashData(Encoding.UTF8.GetBytes("hello"))) + "  greeting-tool\n"));
                ks.Import(badList, "demo", "bad");
                var r1 = Engine.HashFolder(f, new[] { "md5", "sha256" }, new Prog(), ks);
                var r2 = Engine.HashFolder(f, new[] { "sha256" }, new Prog(), null);
                var rows = r1.Rows.ToDictionary(x => x.Path);
                T("folder digest is deterministic", () => r1.Get("sha256") == r2.Get("sha256"));
                T("folder digest follows the ISNAD rule", () => r1.Get("sha256") == Fmt.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(rows.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => $"{k}\t{rows[k].Size}\t{rows[k].Get("sha256")}\n"))))));
                T("signature: PNG named .jpg = mismatch", () => rows["sub/photo.jpg"].SigStatus == "mismatch");
                T("signature: text named .pdf = mismatch", () => rows["sub/fake.pdf"].SigStatus == "mismatch");
                T("signature: real PNG = ok", () => rows["sub/real.png"].SigStatus == "ok");
                T("entropy: random blob flagged", () => rows["vault.bin"].HighEntropy && !rows["a.txt"].HighEntropy);
                T("duplicates detected", () => rows["a.txt"].Duplicates == 1);
                T("known hash set matched", () => rows["a.txt"].Known?.Category == "bad" && r1.Stats["known_bad"] == 2);
                var mf = Path.Combine(root, "m.csv");
                var msha = Engine.WriteManifest(r1, mf);
                var back = Engine.ReadManifest(mf);
                T("manifest CSV round-trip", () => back.Count == r1.Rows.Count && back.All(x => x.Get("sha256") == rows[x.Path].Get("sha256")) && msha == Fmt.Hex(SHA256.HashData(File.ReadAllBytes(mf))));
                File.WriteAllText(Path.Combine(f, "a.txt"), "hellO");
                File.Move(Path.Combine(f, "copy.txt"), Path.Combine(f, "renamed.txt"));
                var r3 = Engine.HashFolder(f, new[] { "sha256" }, new Prog(), null);
                var d = Engine.Diff(r1.Rows, r3.Rows);
                T("folder diff: modified + moved", () => d.Modified.SequenceEqual(new[] { "a.txt" }) && d.Moved.Count == 1 && d.Moved[0].to == "renamed.txt" && !d.Same, () => $"mod={string.Join(",", d.Modified)} moved={d.Moved.Count}");

                var x1 = W("x1.bin", R(100000)); var buf = File.ReadAllBytes(x1); buf[54321] ^= 0xFF; var x2 = W("x2.bin", buf);
                var cmp = Engine.CompareBytes(x1, x2, new Prog());
                T("byte compare finds the exact offset", () => !cmp.Identical && cmp.FirstDiff == 54321 && cmp.DiffBytes == 1);
                T("byte compare identical", () => Engine.CompareBytes(x1, x1, new Prog()).Identical);

                var cases = Path.Combine(root, "cases");
                var cf = CaseFile.Create(cases, new CaseInfo { CaseNo = "T-1", Title = "اختبار", Authority = "warrant" }, "tester");
                var ev = cf.AddEvidence(new Evidence { Kind = "USB", BiosTime = "2026-11-01T08:47:00Z", TrueTime = "2026-11-01T10:00:00Z", SourcePath = f }, "tester",
                    new CustodyEntry { Action = "seized", To = "Sgt A", EventUtc = "2026-11-01T08:00:00Z", Signature = "A" });
                T("BIOS drift computed", () => ev.BiosOffset == -4380 && CustodyRules.Corrected("2026-11-01T14:00:00Z", -4380) == "2026-11-01T15:13:00Z");
                cf.AddCustody(new CustodyEntry { EvidenceId = ev.Id, Action = "transferred", From = "Sgt B", To = "Lab", EventUtc = "2026-11-03T10:00:00Z", SealIntact = "no" }, "tester");
                var acqR = Engine.HashFolder(f, new[] { "sha256" }, new Prog(), null);
                cf.AddHash(acqR, "acquisition", ev.Id, "tester");
                File.WriteAllText(Path.Combine(f, "a.txt"), "changed again");
                var ver = cf.AddHash(Engine.HashFolder(f, new[] { "sha256" }, new Prog(), null), "verification", ev.Id, "tester");
                T("verification detects a changed file", () => ver.Result == "MISMATCH" && ver.Modified.Contains("a.txt"));
                var co = cf.ContinuityOf(ev);
                var codes = co.Findings.Select(x => x.Code).ToList();
                T("continuity: break, seal, long hold, unsigned, mismatch, drift", () => co.Status == "BROKEN" && new[] { "continuity_break", "seal_broken", "long_hold", "no_signature", "hash_mismatch", "clock_drift" }.All(codes.Contains), () => string.Join(",", codes));
                var reopened = new CaseFile(cf.Folder);
                T("ledger replays to the same state", () => reopened.Log.Ok && reopened.Items.Count == 1 && reopened.Custody.Count == 2 && reopened.Hashes.Count == 2 && reopened.Info.Title == "اختبار");
                T("referenced files verified", () => reopened.VerifyFiles().Count == 0);
                var (html, fp) = Report.CaseReport(reopened, "ar");
                var rp = Report.Save(html, Path.Combine(root, "rep"), "r");
                T("report fingerprint verifies", () => Report.Verify(rp).ok);
                File.AppendAllText(rp, "<!-- x -->");
                T("edited report is detected", () => !Report.Verify(rp).ok);
                foreach (var (typ, lang) in new[] { ("e", "ar"), ("e", "en"), ("t", "ar"), ("t", "en") })
                {
                    var (h2, _) = typ == "e" ? Report.EvidenceForm(reopened, reopened.Items[0], lang) : Report.CustodyTag(reopened, reopened.Items[0], lang);
                    var p2 = Report.Save(h2, Path.Combine(root, "rep"), typ + lang);
                    T($"form {typ}/{lang} fingerprint", () => Report.Verify(p2).ok);
                }
                var led = Path.Combine(cf.Folder, "ledger.jsonl");
                var lines = File.ReadAllLines(led);
                lines[2] = lines[2].Replace("Sgt A", "Sgt Z");
                File.WriteAllLines(led, lines);
                var tampered = new CaseFile(cf.Folder);
                T("tampered ledger line is detected", () => !tampered.Log.Ok && tampered.Log.BrokenAt == 3, () => $"ok={tampered.Log.Ok} at={tampered.Log.BrokenAt}");

                // synthetic GPT disk: protective MBR + header + one EFI entry
                var disk = new byte[512 * 40];
                disk[446 + 4] = 0xEE; BitConverter.GetBytes(1u).CopyTo(disk, 446 + 8); BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(disk, 446 + 12);
                disk[510] = 0x55; disk[511] = 0xAA;
                Encoding.ASCII.GetBytes("EFI PART").CopyTo(disk, 512);
                BitConverter.GetBytes(2L).CopyTo(disk, 512 + 72); BitConverter.GetBytes(4u).CopyTo(disk, 512 + 80); BitConverter.GetBytes(128u).CopyTo(disk, 512 + 84);
                new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B").ToByteArray().CopyTo(disk, 1024);
                BitConverter.GetBytes(2048L).CopyTo(disk, 1024 + 32); BitConverter.GetBytes(206847L).CopyTo(disk, 1024 + 40);
                Encoding.Unicode.GetBytes("EFI system partition").CopyTo(disk, 1024 + 56);
                var lay = Partitions.Parse((lba, n) => disk.Skip((int)lba * 512).Take(n * 512).ToArray(), 512, 1_000_000);
                T("GPT parse: protective MBR → EFI entry + gaps", () => lay.Scheme == "GPT" && lay.Parts.Count == 1 && lay.Parts[0].Type == "EFI system partition" && lay.Parts[0].Sectors == 204800 && lay.Gaps.Count >= 1,
                    () => $"{lay.Scheme} {lay.Parts.Count}");
                T("timestamp decoders", () => Interp.FileTime(116444736000000000) == new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) && Fmt.Utc(Interp.UnixSec(1700000000).Value) == "2023-11-14T22:13:20Z" && Interp.WebKit(11644473600000000) == new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                // ---- new tools on synthetic data with known answers
                var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(R(3000)).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
                for (int i = 4; i < jpg.Length - 2; i++) if (jpg[i] == 0xFF) jpg[i] = 0x00; // no stray markers inside
                var blob = new byte[200000];
                Encoding.ASCII.GetBytes("contact: alice@example.com now").CopyTo(blob, 1000);
                Encoding.Unicode.GetBytes("SecretPlan").CopyTo(blob, 5000);
                jpg.CopyTo(blob, 70000);
                Encoding.ASCII.GetBytes("http://evil.example/payload.bin ").CopyTo(blob, 150000);
                var blobP = W("blob.dd", blob);
                var hits = Forensics.Search(blobP, new[] { "secretplan" }, new[] { "FF D8 FF E0" }, new[] { "email", "url" }, true, 0, 0, new Prog());
                T("search: email, URL, UTF-16 keyword, hex", () => hits.Any(h => h.Offset == 1009 && h.Text.Contains("alice@example.com")) && hits.Any(h => h.Offset == 5000 && h.Pattern.Contains("UTF-16"))
                    && hits.Any(h => h.Offset == 70000 && h.Pattern.StartsWith("hex")) && hits.Any(h => h.Offset == 150000), () => string.Join(";", hits.Select(h => h.Offset + ":" + h.Pattern)));
                var carveDir = Path.Combine(root, "carved");
                var carved = Forensics.Carve(blobP, new[] { "JPEG image" }, true, carveDir, 1 << 20, new Prog());
                T("carving: JPEG recovered byte-exact", () => carved.Count == 1 && carved[0].Offset == 70000 && File.ReadAllBytes(carved[0].File).SequenceEqual(jpg), () => $"n={carved.Count}");
                var strs = Forensics.Strings(blobP, 6, true, 0, 0, new Prog());
                T("strings: ASCII and UTF-16", () => strs.Any(s => s.Text.Contains("alice@example.com")) && strs.Any(s => s.Enc == "UTF-16LE" && s.Text == "SecretPlan"));
                var em = Forensics.EntropyMap(bigP, new Prog());
                T("entropy map of random data ≈ 8", () => em.e.Count > 0 && em.e.All(x => x > 7.9));
                var zeros = W("zero.dd", new byte[512 * 64]);
                T("wipe check: zero image is clean", () => Forensics.WipeCheck(zeros, new Prog()).NonZeroSectors == 0);
                T("wipe check: data image is not clean", () => { var w = Forensics.WipeCheck(blobP, new Prog()); return w.NonZeroSectors > 0 && w.FirstNonZero[0] == 1; });
                var imgDir = Path.Combine(root, "img2");
                var im = Forensics.Image(bigP, Path.Combine(imgDir, "big.dd"), 3 << 20, new[] { "md5", "sha256" }, true, new Prog(), "tester", "self-test");
                T("imaging: split dd image verified", () => im.Verified == true && im.Files.Count == 3 && im.SourceHash["sha256"] == hb.Get("sha256") && File.Exists(im.Log));
                var rejoined = Engine.HashFile(im.Files[0], new[] { "sha256" }, new Prog(), true);
                T("imaging: segments rejoin to the source hash", () => rejoined.Get("sha256") == hb.Get("sha256"));
                var cs = Forensics.ChecksumText(r2, "sha256");
                File.WriteAllText(Path.Combine(root, "f", "list.sha256"), cs);
                File.WriteAllText(Path.Combine(f, "vault.bin"), "tampered");
                var chk = Forensics.VerifyChecksums(Path.Combine(root, "f", "list.sha256"), new Prog());
                T("checksum file: detects the changed and the missing file", () => chk.Count(x => x.Status == "OK") >= 2 && chk.Single(x => x.Path == "vault.bin").Status == "FAILED" && chk.Single(x => x.Path == "copy.txt").Status == "MISSING",
                    () => string.Join(",", chk.Select(x => x.Path + "=" + x.Status)));
                var flt = Engine.HashFolder(f, new[] { "sha256" }, new Prog(), null, Engine.Filter.Parse("*.png;*.txt", false, false, 0));
                T("folder filter by type", () => flt.Rows.Count > 0 && flt.Rows.All(x => x.Path.EndsWith(".png") || x.Path.EndsWith(".txt")) && flt.Note.StartsWith("filter"));
                var (crec, _) = Report.CustodyRecord(reopened, reopened.Items, "ar", false);
                var crp = Report.Save(crec, Path.Combine(root, "rep"), "coc_full");
                T("complete custody record: fingerprint + all sections", () => Report.Verify(crp).ok && crec.Contains("RFC 3227") && crec.Contains("ACPO") && crec.Contains("ISO/IEC 27037") && crec.Contains("sigcell"));
                var (cblank, _) = Report.CustodyRecord(null, null, "en", true);
                var cbp = Report.Save(cblank, Path.Combine(root, "rep"), "coc_blank");
                T("blank custody form", () => Report.Verify(cbp).ok && cblank.Contains("Chain of Custody Record") && System.Text.RegularExpressions.Regex.Matches(cblank, "class=\"wet\"").Count >= 30);

                var dl = Disks.List();
                T("disk enumeration (no admin needed)", () => dl.Count >= 1 && dl[0].Size > 0, () => $"disks={dl.Count}");
                if (!Native.IsAdmin())
                    T("raw read without admin is refused cleanly", () => { try { Disks.ReadAt(dl[0].Path, 0, 512); return false; } catch (UnauthorizedAccessException) { return true; } });
                else
                    T("read LBA 0 of PhysicalDrive0 (admin)", () => { var s0 = Disks.ReadAt(dl[0].Path, 0, 512); return s0.Length == 512 && s0[510] == 0x55 && s0[511] == 0xAA; });
                T("hash text equals file hash", () => { var p = W("t.txt", Encoding.UTF8.GetBytes("نص عربي")); return Engine.HashFile(p, new[] { "sha256" }, new Prog()).Get("sha256") == Engine.HashText("نص عربي", new[] { "sha256" }).Get("sha256"); });
            }
            finally { try { Directory.Delete(root, true); } catch { } }
            if (logPath != null) File.WriteAllLines(logPath, res.Select(r => (r.Item2 ? "PASS " : "FAIL ") + r.Item1 + (r.Item2 ? "" : "  — " + r.Item3)).Append($"{res.Count(r => r.Item2)}/{res.Count} passed"));
            return res;
        }
    }
}
