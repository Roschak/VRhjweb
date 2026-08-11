using HajjVR.Services;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        var rnd = new Random(42);

        // ---------- Users ----------
        var users = new List<AppUser>
        {
            NewUser("admin", "admin@hajjvr.app", "Administrator Sistem", Roles.Admin),
            NewUser("operator", "operator@hajjvr.app", "Operator Aplikasi", Roles.Operator),
            NewUser("ustadz.ahmad", "ahmad@hajjvr.app", "Ust. Ahmad Fauzi", Roles.Pembimbing),
            NewUser("ustadzah.siti", "siti@hajjvr.app", "Ustzh. Siti Maryam", Roles.Pembimbing),
            NewUser("ustadz.ridwan", "ridwan@hajjvr.app", "Ust. Ridwan Kamaluddin", Roles.Pembimbing),
        };

        string[] firstNames = ["Budi", "Andi", "Dewi", "Rina", "Agus", "Sri", "Joko", "Fitri", "Hendra", "Lina",
            "Yusuf", "Aisyah", "Rahmat", "Nur", "Dian", "Eko", "Wati", "Bambang", "Ratna", "Slamet",
            "Umar", "Halimah", "Zainal", "Kartika", "Fauzan", "Maya", "Irfan", "Salma", "Taufik", "Indah"];
        string[] lastNames = ["Santoso", "Wijaya", "Hidayat", "Rahayu", "Pratama", "Lestari", "Nugroho", "Sari",
            "Kusuma", "Utami", "Ramadhan", "Puspita", "Saputra", "Anggraini", "Firmansyah"];
        string[] groups = ["KBIH Al-Hikmah", "KBIH Ar-Rahman", "KBIH An-Nur", "Travel Madinah Iman", "Travel Barokah"];

        for (int i = 0; i < 40; i++)
        {
            var name = $"{firstNames[i % firstNames.Length]} {lastNames[(i * 7) % lastNames.Length]}";
            var uname = $"jamaah{i + 1:00}";
            users.Add(NewUser(uname, $"{uname}@hajjvr.app", name, Roles.Jamaah));
        }
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var pembimbings = users.Where(u => u.Role == Roles.Pembimbing).ToList();
        var jamaahs = users.Where(u => u.Role == Roles.Jamaah).ToList();

        // ---------- Profiles ----------
        foreach (var (j, i) in jamaahs.Select((j, i) => (j, i)))
        {
            db.JamaahProfiles.Add(new JamaahProfile
            {
                UserId = j.Id,
                GroupName = groups[i % groups.Length],
                PembimbingUserId = pembimbings[i % pembimbings.Count].Id,
                Nationality = "Indonesia",
                PassportNumber = $"C{1000000 + i * 137}",
                BirthDate = new DateTime(1960 + rnd.Next(40), rnd.Next(1, 13), rnd.Next(1, 28)),
                Phone = $"+62 81{rnd.Next(10000000, 99999999)}",
                PackageType = i % 3 == 0 ? "Haji" : "Umrah",
                Notes = i % 5 == 0 ? "Lansia, perlu pendampingan kursi roda." : null
            });
        }

        // ---------- Ritual progress ----------
        var allRituals = Enum.GetValues<RitualType>();
        foreach (var j in jamaahs)
        {
            int completedUpTo = rnd.Next(0, allRituals.Length + 1);
            foreach (var (r, idx) in allRituals.Select((r, idx) => (r, idx)))
            {
                var status = idx < completedUpTo ? ProgressStatus.Completed
                    : idx == completedUpTo ? ProgressStatus.InProgress
                    : ProgressStatus.NotStarted;
                db.RitualProgresses.Add(new RitualProgress
                {
                    UserId = j.Id,
                    Ritual = r,
                    Status = status,
                    StartedAt = status != ProgressStatus.NotStarted ? DateTime.UtcNow.AddDays(-rnd.Next(1, 20)) : null,
                    CompletedAt = status == ProgressStatus.Completed ? DateTime.UtcNow.AddDays(-rnd.Next(0, 10)) : null,
                    DurationMinutes = status == ProgressStatus.Completed ? rnd.Next(20, 180) : 0
                });
            }
        }

        // ---------- Locations ----------
        db.Locations.AddRange(
            Loc("Ka'bah", "الكعبة", "Kiblat umat Islam, bangunan suci di tengah Masjidil Haram. Titik pusat ibadah thawaf.", 21.4225, 39.8262, "Ritual", "haram", "mataf"),
            Loc("Hajar Aswad", "الحجر الأسود", "Batu hitam di sudut timur Ka'bah, titik awal thawaf.", 21.4224, 39.8264, "Ritual", "haram", "mataf"),
            Loc("Maqam Ibrahim", "مقام إبراهيم", "Tempat berdirinya Nabi Ibrahim saat membangun Ka'bah. Sunnah shalat 2 rakaat setelah thawaf di belakangnya.", 21.4226, 39.8263, "Ritual", "haram", "mataf"),
            Loc("Hijr Ismail", "حجر إسماعيل", "Area setengah lingkaran di sisi utara Ka'bah, bagian dari Ka'bah — thawaf harus di luarnya.", 21.4227, 39.8261, "Ritual", "haram", "mataf"),
            Loc("Bukit Safa", "الصفا", "Titik awal ibadah sa'i, berjarak sekitar 450 m dari Marwah.", 21.4211, 39.8276, "Ritual", "haram", "masaa"),
            Loc("Bukit Marwah", "المروة", "Titik akhir sa'i. Perjalanan Safa-Marwah 7 kali ± 3,15 km.", 21.4237, 39.8290, "Ritual", "haram", "masaa"),
            Loc("Sumur Zamzam", "زمزم", "Sumber air zamzam yang tidak pernah kering sejak zaman Nabi Ismail.", 21.4223, 39.8266, "Ziarah", "haram", "mataf"),
            Loc("Masjidil Haram", "المسجد الحرام", "Masjid terbesar di dunia yang mengelilingi Ka'bah, kapasitas lebih dari 2 juta jamaah.", 21.4225, 39.8262, "Masjid", "haram", "mosque"),
            Loc("Padang Arafah", "عرفات", "Tempat wukuf 9 Dzulhijjah, rukun haji terpenting. 'Haji adalah Arafah.'", 21.3549, 39.9841, "Ritual", "manasik", "arafah"),
            Loc("Jabal Rahmah", "جبل الرحمة", "Bukit kasih sayang di Arafah, tempat bertemunya Adam dan Hawa.", 21.3552, 39.9846, "Ziarah", "manasik", "arafah"),
            Loc("Muzdalifah", "مزدلفة", "Tempat mabit (bermalam) setelah wukuf dan mengumpulkan batu kerikil untuk lempar jumrah.", 21.3838, 39.9367, "Ritual", "manasik", "muzdalifah"),
            Loc("Jamarat Mina", "الجمرات", "Tempat melempar jumrah: Ula, Wustha, dan Aqabah di Mina.", 21.4211, 39.8723, "Ritual", "manasik", "mina"),
            Loc("Tenda Mina", "منى", "Kota tenda tempat mabit jamaah haji pada hari-hari Tasyrik.", 21.4133, 39.8933, "Fasilitas", "manasik", "mina"),
            Loc("Masjid Nabawi", "المسجد النبوي", "Masjid Nabi Muhammad ﷺ di Madinah, masjid kedua paling utama.", 24.4672, 39.6111, "Masjid", "nabawi", "nabawi"),
            Loc("Raudhah", "الروضة الشريفة", "Taman surga antara mimbar dan rumah Nabi ﷺ, tempat mustajab untuk berdoa.", 24.4675, 39.6113, "Ziarah", "nabawi", "raudhah"),
            Loc("Makam Rasulullah ﷺ", "القبر الشريف", "Makam Nabi Muhammad ﷺ beserta Abu Bakar dan Umar radhiyallahu 'anhuma.", 24.4676, 39.6115, "Ziarah", "nabawi", "raudhah"),
            Loc("Mina", "منى", "Lembah tempat mabit dan lempar jumrah pada 10-13 Dzulhijjah.", 21.4133, 39.8933, "Ritual", "manasik", "mina")
        );

        // ---------- Badges ----------
        var badges = new List<Badge>
        {
            new() { Code = "first-step", Name = "Langkah Pertama", Description = "Memulai ritual pertama", Icon = "👣", Points = 5 },
            new() { Code = "thawaf-master", Name = "Thawaf Sempurna", Description = "Menyelesaikan thawaf 7 putaran", Icon = "🕋", Points = 20 },
            new() { Code = "sai-runner", Name = "Pelari Sa'i", Description = "Menyelesaikan sa'i Safa-Marwah", Icon = "🏃", Points = 20 },
            new() { Code = "wukuf-arafah", Name = "Wukuf Arafah", Description = "Menunaikan wukuf di Arafah", Icon = "⛰️", Points = 30 },
            new() { Code = "jumrah-warrior", Name = "Pelempar Jumrah", Description = "Menyelesaikan lempar jumrah", Icon = "🎯", Points = 20 },
            new() { Code = "umrah-complete", Name = "Umrah Mabrur", Description = "Menyelesaikan seluruh rangkaian umrah", Icon = "🌙", Points = 50 },
            new() { Code = "hajj-complete", Name = "Haji Mabrur", Description = "Menyelesaikan seluruh rangkaian haji", Icon = "🏆", Points = 100 },
            new() { Code = "explorer", Name = "Penjelajah Suci", Description = "Mengunjungi semua lokasi 3D", Icon = "🧭", Points = 15 },
        };
        db.Badges.AddRange(badges);
        await db.SaveChangesAsync();

        // Award sample badges based on progress
        var progresses = await db.RitualProgresses.Where(p => p.Status == ProgressStatus.Completed).ToListAsync();
        foreach (var g in progresses.GroupBy(p => p.UserId))
        {
            var done = g.Select(p => p.Ritual).ToHashSet();
            void Award(string code) => db.UserBadges.Add(new UserBadge { UserId = g.Key, BadgeId = badges.First(b => b.Code == code).Id });
            if (done.Count > 0) Award("first-step");
            if (done.Contains(RitualType.Thawaf)) Award("thawaf-master");
            if (done.Contains(RitualType.Sai)) Award("sai-runner");
            if (done.Contains(RitualType.WukufArafah)) Award("wukuf-arafah");
            if (done.Contains(RitualType.LemparJumrah)) Award("jumrah-warrior");
            if (done.Contains(RitualType.Ihram) && done.Contains(RitualType.Thawaf) && done.Contains(RitualType.Sai) && done.Contains(RitualType.Tahalul)) Award("umrah-complete");
        }

        // ---------- Knowledge documents (panduan manasik untuk pencarian semantik & chatbot) ----------
        var adminId = users.First(u => u.Role == Roles.Admin).Id;
        db.Documents.AddRange(
            Doc(adminId, "Panduan Thawaf", """
                Thawaf adalah mengelilingi Ka'bah sebanyak 7 putaran berlawanan arah jarum jam, dimulai dan diakhiri di garis sejajar Hajar Aswad.
                Syarat thawaf: suci dari hadas, menutup aurat, di dalam Masjidil Haram, Ka'bah di sebelah kiri.
                Sunnah: berlari kecil (raml) pada 3 putaran pertama bagi laki-laki (thawaf qudum/umrah), istilam Hajar Aswad, membaca doa antara Rukun Yamani dan Hajar Aswad: Rabbana atina fid-dunya hasanah wa fil-akhirati hasanah wa qina 'adzaban-nar.
                Setelah thawaf disunnahkan shalat 2 rakaat di belakang Maqam Ibrahim dan minum air zamzam.
                """),
            Doc(adminId, "Panduan Sa'i", """
                Sa'i adalah berjalan dari bukit Safa ke Marwah sebanyak 7 kali perjalanan (Safa→Marwah dihitung 1, Marwah→Safa dihitung 1), diakhiri di Marwah.
                Sa'i dilakukan setelah thawaf. Jarak satu lintasan sekitar 450 meter, total sekitar 3,15 km.
                Laki-laki disunnahkan berlari kecil di antara dua lampu hijau. Tidak disyaratkan suci dari hadas, namun lebih utama dalam keadaan suci.
                Doa di Safa dan Marwah: menghadap kiblat, bertakbir dan berdoa. Innash-shafa wal-marwata min sya'airillah.
                """),
            Doc(adminId, "Panduan Ihram dan Miqat", """
                Ihram adalah niat memasuki ibadah haji atau umrah dengan mengenakan pakaian ihram dari miqat.
                Miqat makani antara lain: Dzul Hulaifah (Bir Ali) untuk penduduk Madinah, Yalamlam untuk arah Yaman, Qarnul Manazil, Juhfah, dan Dzatu Irqin.
                Larangan ihram: memakai pakaian berjahit (laki-laki), menutup kepala (laki-laki), memakai wewangian, memotong kuku dan rambut, berburu, melamar/menikah, dan jima'.
                Talbiyah: Labbaik Allahumma labbaik, labbaika laa syarika laka labbaik, innal hamda wan-ni'mata laka wal-mulk, laa syarika lak.
                """),
            Doc(adminId, "Panduan Wukuf di Arafah", """
                Wukuf di Arafah adalah rukun haji terpenting, dilaksanakan pada 9 Dzulhijjah dari tergelincir matahari (zhuhur) hingga terbenam.
                Nabi ﷺ bersabda: "Al-hajju 'Arafah" (Haji adalah Arafah). Jamaah yang tidak wukuf maka hajinya tidak sah.
                Amalan saat wukuf: memperbanyak doa, dzikir, talbiyah, istighfar, dan membaca Al-Quran. Shalat zhuhur dan ashar dijamak taqdim dan diqashar.
                Doa terbaik adalah doa hari Arafah: Laa ilaha illallah wahdahu laa syarika lah, lahul-mulku wa lahul-hamdu wa huwa 'ala kulli syai'in qadir.
                """),
            Doc(adminId, "Panduan Mabit di Muzdalifah dan Mina", """
                Setelah wukuf, jamaah bergerak ke Muzdalifah setelah maghrib untuk mabit (bermalam) hingga lewat tengah malam, shalat maghrib-isya dijamak.
                Di Muzdalifah jamaah mengumpulkan batu kerikil (minimal 7 untuk Aqabah, total 49-70 butir untuk seluruh hari tasyrik).
                Mabit di Mina dilakukan pada malam 11, 12 (nafar awal), dan 13 Dzulhijjah (nafar tsani). Mabit hukumnya wajib haji; meninggalkannya wajib membayar dam.
                """),
            Doc(adminId, "Panduan Lempar Jumrah", """
                Lempar jumrah dilakukan di Mina: Jumrah Aqabah pada 10 Dzulhijjah dengan 7 batu, kemudian pada hari tasyrik (11-13 Dzulhijjah) melempar tiga jumrah: Ula, Wustha, dan Aqabah masing-masing 7 batu setelah zawal.
                Setiap lemparan disertai takbir. Batu harus mengenai lubang jamarat. Boleh diwakilkan bagi yang udzur (sakit, lansia).
                Setelah jumrah Aqabah 10 Dzulhijjah dilanjutkan menyembelih hadyu, tahalul awal (cukur), lalu thawaf ifadah.
                """),
            Doc(adminId, "Panduan Ziarah Masjid Nabawi dan Raudhah", """
                Ziarah ke Masjid Nabawi di Madinah bukan bagian dari rukun haji/umrah namun sangat dianjurkan. Shalat di Masjid Nabawi bernilai 1000 kali shalat di masjid lain.
                Raudhah adalah area antara mimbar dan rumah (kamar) Nabi ﷺ yang disebut taman di antara taman-taman surga. Masuk Raudhah kini diatur melalui aplikasi Nusuk.
                Adab ziarah makam Nabi ﷺ: mengucap salam dengan suara pelan, tidak mengusap dinding, menjaga ketenangan. Salam juga kepada Abu Bakar dan Umar radhiyallahu 'anhuma.
                """),
            Doc(adminId, "Rukun dan Wajib Umrah", """
                Rukun umrah: 1) Ihram (niat), 2) Thawaf, 3) Sa'i, 4) Tahalul (mencukur/memendekkan rambut), 5) Tertib.
                Wajib umrah: ihram dari miqat dan menjauhi larangan ihram. Meninggalkan wajib dikenakan dam, meninggalkan rukun umrah tidak sah.
                Urutan umrah: ihram dari miqat → masuk Masjidil Haram → thawaf 7 putaran → shalat 2 rakaat di Maqam Ibrahim → minum zamzam → sa'i 7 lintasan → tahalul.
                """),
            Doc(adminId, "Rukun dan Wajib Haji", """
                Rukun haji: 1) Ihram, 2) Wukuf di Arafah, 3) Thawaf Ifadah, 4) Sa'i, 5) Tahalul, 6) Tertib.
                Wajib haji: ihram dari miqat, mabit di Muzdalifah, mabit di Mina, lempar jumrah, thawaf wada'.
                Jenis haji: Tamattu' (umrah dulu lalu haji, wajib dam), Ifrad (haji saja), Qiran (haji dan umrah sekaligus, wajib dam).
                Jadwal ringkas: 8 Dzulhijjah (Tarwiyah) ke Mina, 9 Dzulhijjah wukuf di Arafah lalu mabit Muzdalifah, 10 Dzulhijjah lempar Aqabah-kurban-tahalul-thawaf ifadah, 11-13 Dzulhijjah mabit Mina dan lempar tiga jumrah, ditutup thawaf wada'.
                """)
        );

        // ---------- Crowd snapshots (48 jam terakhir, per zona) ----------
        string[] zones = ["mataf", "masaa", "mosque", "arafah", "muzdalifah", "mina", "nabawi", "raudhah"];
        var now = DateTime.UtcNow;
        foreach (var zone in zones)
        {
            int baseCount = zone switch
            {
                "mataf" => 35000, "masaa" => 18000, "mosque" => 90000,
                "arafah" => 60000, "muzdalifah" => 25000, "mina" => 45000,
                "nabawi" => 50000, "raudhah" => 800, _ => 5000
            };
            for (int h = 48; h >= 0; h--)
            {
                var t = now.AddHours(-h);
                // pola harian: ramai waktu shalat (subuh, zhuhur, maghrib, isya)
                double hour = t.Hour;
                double prayerBoost = Math.Exp(-Math.Pow(hour - 5, 2) / 4) + Math.Exp(-Math.Pow(hour - 12.5, 2) / 4)
                                   + Math.Exp(-Math.Pow(hour - 18.5, 2) / 3) + Math.Exp(-Math.Pow(hour - 20, 2) / 4);
                int count = (int)(baseCount * (0.55 + 0.45 * Math.Min(1, prayerBoost)) * (0.9 + rnd.NextDouble() * 0.2));
                db.CrowdSnapshots.Add(new CrowdSnapshot { Zone = zone, Count = count, Timestamp = t });
            }
        }

        // ---------- Activity logs ----------
        string[] actions = ["Login", "MulaiRitual", "SelesaiRitual", "BukaSimulasi3D", "ChatHajiSule", "LihatPeta", "UploadDokumen"];
        for (int i = 0; i < 200; i++)
        {
            var u = jamaahs[rnd.Next(jamaahs.Count)];
            db.ActivityLogs.Add(new ActivityLog
            {
                UserId = u.Id,
                Action = actions[rnd.Next(actions.Length)],
                Detail = $"Aktivitas otomatis contoh #{i + 1}",
                Timestamp = now.AddMinutes(-rnd.Next(0, 60 * 72))
            });
        }

        await db.SaveChangesAsync();
    }

    private static AppUser NewUser(string userName, string email, string displayName, string role) => new()
    {
        UserName = userName,
        Email = email,
        DisplayName = displayName,
        Role = role,
        // Password default demo: {username}123 — mis. admin123
        PasswordHash = PasswordHasher.Hash($"{userName}123"),
        Language = "id",
        CreatedAt = DateTime.UtcNow.AddDays(-30)
    };

    private static Location Loc(string name, string ar, string desc, double lat, double lng, string cat, string scene, string zone)
        => new() { Name = name, NameArabic = ar, Description = desc, Latitude = lat, Longitude = lng, Category = cat, SceneKey = scene, Zone = zone };

    private static JamaahDocument Doc(int userId, string title, string content) => new()
    {
        UserId = userId,
        Title = title,
        FileName = $"{title.ToLowerInvariant().Replace(' ', '-').Replace("'", "")}.md",
        Url = "",
        ContentType = "text/markdown",
        SizeBytes = content.Length,
        ContentText = content,
        Kind = "Panduan",
        UploadedAt = DateTime.UtcNow.AddDays(-15)
    };
}
