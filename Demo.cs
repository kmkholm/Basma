using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Basma
{
    /// <summary>--demo: builds a fictional teaching case (refuses if the data folder already has cases).</summary>
    public static class Demo
    {
        public static void Build()
        {
            if (Directory.GetDirectories(Store.CasesDir).Length > 0) throw new InvalidOperationException("data folder already has cases");
            Store.S.Examiner = "د. محمد توفيق"; Store.S.Agency = "مختبر الأدلة الرقمية — جامعة عجلون الوطنية (تدريب)"; Store.Save();
            var src = Path.Combine(Store.Data, "demo_sources");
            var rnd = new Random(7);
            byte[] R(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }
            void W(string rel, byte[] d) { var p = Path.Combine(src, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, d); }
            byte[] zip(int n) => new byte[] { 0x50, 0x4B, 3, 4 }.Concat(R(n)).ToArray();
            W("USB_E001/Contracts/contract_2026_A.docx", zip(9000)); W("USB_E001/Contracts/contract_2026_B.docx", zip(7000)); W("USB_E001/Contracts/pricing.xlsx", zip(5000));
            W("USB_E001/holiday.jpg", zip(6000));
            W("USB_E001/notes.txt", Encoding.UTF8.GetBytes("Meet K. at the cafe 21:00 — bring the contracts.\n"));
            W("USB_E001/notes_copy.txt", Encoding.UTF8.GetBytes("Meet K. at the cafe 21:00 — bring the contracts.\n"));
            W("USB_E001/system.dat", R(400000));
            var wiper = new byte[] { 0x4D, 0x5A }.Concat(Enumerable.Repeat((byte)0x90, 4000)).Concat(Encoding.ASCII.GetBytes("wipe-tool")).ToArray();
            W("USB_E001/tools/wiper.exe", wiper);
            for (int i = 1; i <= 3; i++) W($"laptop_image/LAPTOP-E002.{i:D3}", R(300000));
            W("known_bad_tools.txt", Encoding.UTF8.GetBytes(Fmt.Hex(MD5.HashData(wiper)) + "  WiperTool v2 (anti-forensics)\n"));
            Store.Known.Import(Path.Combine(src, "known_bad_tools.txt"), "Anti-forensics tools (demo)", "bad");

            string me = Store.S.Examiner;
            var c = CaseFile.Create(Store.CasesDir, new CaseInfo
            {
                CaseNo = "CASE-2026-014", Title = "تسريب عقود الشركة عبر ذاكرة USB (حالة تدريبية)", Type = "سرقة بيانات", Agency = Store.S.Agency, Lead = me,
                Authority = "مذكرة تفتيش من المدعي العام", AuthorityRef = "MT-2026/883", Opened = "2026-11-10T06:30:00Z",
                Description = "بلاغ من الشركة بأن موظفاً نسخ عقوداً سرية قبل استقالته. القضية خيالية لأغراض التدريب."
            }, me);
            foreach (var (s, n) in new[] { ("authority", "MT-2026/883 — نطاق: مكتب الموظف وأجهزته"), ("photo_scene", "12 صورة للمكتب والشاشة"), ("encryption", "لا BitLocker"), ("memory", "تفريغ 16GB إلى USB المحقق"), ("power", "سحب القابس 08:05"), ("label", "تم"), ("bios", "BIOS متأخر 73 دقيقة") })
                c.SetCheck(s, true, me, n, me);
            var e1 = c.AddEvidence(new Evidence
            {
                ItemNo = "E001", Kind = "ذاكرة USB", Make = "SanDisk", Model = "Ultra Fit", Serial = "4C530001230821111283", Capacity = "32 GB", Description = "ذاكرة USB سوداء موصولة بالحاسوب لحظة الدخول",
                Condition = "خدش خفيف على الغطاء", SeizedFrom = "الموظف س. ع.", SeizedLocation = "مكتب 214، الطابق الثاني", SeizedBy = "الرقيب أحمد", SeizedUtc = "2026-11-10T08:10:00Z", SealNo = "BAG-0441",
                SourcePath = Path.Combine(src, "USB_E001")
            }, me, new CustodyEntry { Action = "seized", From = "الموظف س. ع.", To = "الرقيب أحمد", EventUtc = "2026-11-10T08:10:00Z", Location = "مكتب 214", SealIntact = "yes", SealNo = "BAG-0441", Signature = "الرقيب أحمد", Witness = "مدير الأمن" });
            void Cu(int id, string a, string f, string t, string when, string loc, string seal, string sig, string pur = null, string wit = null) =>
                c.AddCustody(new CustodyEntry { EvidenceId = id, Action = a, From = f, To = t, EventUtc = when, Location = loc, SealIntact = seal, Signature = sig, Purpose = pur, Witness = wit, SealNo = "BAG-0441" }, me);
            Cu(e1.Id, "packaged", "الرقيب أحمد", "الرقيب أحمد", "2026-11-10T08:20:00Z", "مكتب 214", "yes", "الرقيب أحمد", "تحريز في كيس مضاد للكهرباء الساكنة");
            Cu(e1.Id, "transported", "الرقيب أحمد", "الرقيب أحمد", "2026-11-10T08:35:00Z", "سيارة الوحدة → المختبر", "yes", "الرقيب أحمد", "نقل مباشر دون توقف");
            Cu(e1.Id, "transferred", "الرقيب أحمد", "أمين الأدلة", "2026-11-10T09:05:00Z", "مختبر الأدلة", "yes", "أمين الأدلة", null, "الرقيب أحمد");
            Cu(e1.Id, "stored", "أمين الأدلة", "أمين الأدلة", "2026-11-10T09:10:00Z", "خزانة 07", "yes", "أمين الأدلة");
            Cu(e1.Id, "checked_out", "أمين الأدلة", me, "2026-11-11T07:00:00Z", "محطة الفحص 2", "yes", me, "التصوير الجنائي عبر حاجب كتابة Tableau");
            c.AddHash(Engine.HashFolder(e1.SourcePath, new[] { "md5", "sha1", "sha256" }, new Prog(), Store.Known), "acquisition", e1.Id, me);
            Cu(e1.Id, "imaged", "", "", "2026-11-11T07:40:00Z", "محطة الفحص 2", "", me, "هاش الاستحواذ + بيان الملفات");
            c.AddHash(Engine.HashFolder(e1.SourcePath, new[] { "md5", "sha1", "sha256" }, new Prog(), Store.Known), "verification", e1.Id, me);
            Cu(e1.Id, "returned", me, "أمين الأدلة", "2026-11-11T09:00:00Z", "خزانة 07", "yes", "أمين الأدلة");

            var img = Path.Combine(src, "laptop_image", "LAPTOP-E002.001");
            var e2 = c.AddEvidence(new Evidence
            {
                ItemNo = "E002", Kind = "صورة جنائية E01/dd", Make = "Dell", Model = "Latitude 5440", Serial = "7XK2QF3", Capacity = "512 GB", Description = "صورة مقسمة لقرص الحاسوب المحمول (dd مجزأة)",
                SeizedFrom = "الموظف س. ع.", SeizedLocation = "مكتب 214", SeizedBy = "الرقيب أحمد", SeizedUtc = "2026-11-10T08:15:00Z", SealNo = "BAG-0442",
                BiosTime = "2026-11-10T07:52:00Z", TrueTime = "2026-11-10T09:05:00Z", SourcePath = img, Split = true
            }, me, new CustodyEntry { Action = "seized", From = "الموظف س. ع.", To = "الرقيب أحمد", EventUtc = "2026-11-10T08:15:00Z", Location = "مكتب 214", SealIntact = "yes", SealNo = "BAG-0442", Signature = "الرقيب أحمد" });
            c.AddCustody(new CustodyEntry { EvidenceId = e2.Id, Action = "transferred", From = "الرقيب خالد", To = "أمين الأدلة", EventUtc = "2026-11-12T10:00:00Z", Location = "مختبر الأدلة", SealIntact = "no", Purpose = "تسليم متأخر" }, me);
            c.AddHash(Engine.HashFile(img, new[] { "md5", "sha1", "sha256" }, new Prog(), true), "acquisition", e2.Id, me);
            using (var fs = new FileStream(Path.Combine(src, "laptop_image", "LAPTOP-E002.002"), FileMode.Open, FileAccess.ReadWrite)) { fs.Position = 1234; fs.WriteByte(0); }
            c.AddHash(Engine.HashFile(img, new[] { "md5", "sha1", "sha256" }, new Prog(), true), "verification", e2.Id, me);
            c.AddNote("بيان E001 يُظهر holiday.jpg بتوقيع ZIP (امتداد كاذب)، وsystem.dat بإنتروبيا عالية (حاوية مشفرة محتملة)، وwiper.exe مطابقاً لقائمة أدوات مكافحة التحليل.", me);
            var hr = Engine.HashFile(img, new[] { "md5", "sha256" }, new Prog(), true);
            Store.AddHistory(hr, "quick", "COMPUTED", null);
        }
    }
}
