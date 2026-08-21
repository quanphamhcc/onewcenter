namespace WorshipPlayer.Core.Services.Bible;

/// <summary>
/// Provides SQL statements for creating the Bible database schema and seeding
/// translation metadata for KJV and WEB (both public domain).
/// </summary>
public static class BibleSeedData
{
    /// <summary>
    /// Generate the DDL SQL to create all Bible database tables.
    /// </summary>
    public static string GenerateSchemaSQL()
    {
        return @"
CREATE TABLE IF NOT EXISTS Translations (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Abbreviation TEXT NOT NULL,
    Language TEXT NOT NULL DEFAULT 'en',
    IsDefault INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Books (
    Id INTEGER NOT NULL,
    Name TEXT NOT NULL,
    Abbreviation TEXT NOT NULL,
    Testament TEXT NOT NULL,
    ChapterCount INTEGER NOT NULL DEFAULT 0,
    BookOrder INTEGER NOT NULL DEFAULT 0,
    TranslationId TEXT NOT NULL,
    PRIMARY KEY (Id, TranslationId),
    FOREIGN KEY (TranslationId) REFERENCES Translations(Id)
);

CREATE TABLE IF NOT EXISTS Verses (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BookId INTEGER NOT NULL,
    Chapter INTEGER NOT NULL,
    Verse INTEGER NOT NULL,
    Text TEXT NOT NULL,
    TranslationId TEXT NOT NULL,
    FOREIGN KEY (TranslationId) REFERENCES Translations(Id)
);

CREATE INDEX IF NOT EXISTS idx_verses_lookup ON Verses(TranslationId, BookId, Chapter, Verse);
CREATE INDEX IF NOT EXISTS idx_verses_text ON Verses(Text);
";
    }

    /// <summary>
    /// Generate SQL INSERT statements for KJV and WEB translation records.
    /// These are the two bundled public domain translations.
    /// </summary>
    public static string SeedTranslationsSQL()
    {
        return @"
INSERT OR IGNORE INTO Translations (Id, Name, Abbreviation, Language, IsDefault)
VALUES ('KJV', 'King James Version', 'KJV', 'en', 1);

INSERT OR IGNORE INTO Translations (Id, Name, Abbreviation, Language, IsDefault)
VALUES ('WEB', 'World English Bible', 'WEB', 'en', 0);
";
    }

    /// <summary>
    /// Generate SQL INSERT statements for the standard 66-book canon.
    /// Books are numbered 1-66 in canonical order.
    /// Called once per translation to populate its book records.
    /// </summary>
    public static string SeedBooksSQL(string translationId)
    {
        var books = GetBookData();
        var sql = new System.Text.StringBuilder();

        foreach (var (id, name, abbreviation, testament, chapterCount) in books)
        {
            var escapedName = name.Replace("'", "''");
            sql.AppendLine($"INSERT OR IGNORE INTO Books (Id, Name, Abbreviation, Testament, ChapterCount, BookOrder, TranslationId) VALUES ({id}, '{escapedName}', '{abbreviation}', '{testament}', {chapterCount}, {id}, '{translationId}');");
        }

        return sql.ToString();
    }

    /// <summary>
    /// Generate SQL INSERT statements for KJV verse text for Genesis 1, Psalms 23, John 3, and Romans 8.
    /// Provides testable lookup data for the scripture browser.
    /// </summary>
    public static string SeedKjvVersesSQL()
    {
        var sql = new System.Text.StringBuilder();

        // Genesis 1:1-31 (Book ID 1)
        var genesis1 = new[]
        {
            "In the beginning God created the heaven and the earth.",
            "And the earth was without form, and void; and darkness was upon the face of the deep. And the Spirit of God moved upon the face of the waters.",
            "And God said, Let there be light: and there was light.",
            "And God saw the light, that it was good: and God divided the light from the darkness.",
            "And God called the light Day, and the darkness he called Night. And the evening and the morning were the first day.",
            "And God said, Let there be a firmament in the midst of the waters, and let it divide the waters from the waters.",
            "And God made the firmament, and divided the waters which were under the firmament from the waters which were above the firmament: and it was so.",
            "And God called the firmament Heaven. And the evening and the morning were the second day.",
            "And God said, Let the waters under the heaven be gathered together unto one place, and let the dry land appear: and it was so.",
            "And God called the dry land Earth; and the gathering together of the waters called he Seas: and God saw that it was good.",
            "And God said, Let the earth bring forth grass, the herb yielding seed, and the fruit tree yielding fruit after his kind, whose seed is in itself, upon the earth: and it was so.",
            "And the earth brought forth grass, and herb yielding seed after his kind, and the tree yielding fruit, whose seed was in itself, after his kind: and God saw that it was good.",
            "And the evening and the morning were the third day.",
            "And God said, Let there be lights in the firmament of the heaven to divide the day from the night; and let them be for signs, and for seasons, and for days, and years:",
            "And let them be for lights in the firmament of the heaven to give light upon the earth: and it was so.",
            "And God made two great lights; the greater light to rule the day, and the lesser light to rule the night: he made the stars also.",
            "And God set them in the firmament of the heaven to give light upon the earth,",
            "And to rule over the day and over the night, and to divide the light from the darkness: and God saw that it was good.",
            "And the evening and the morning were the fourth day.",
            "And God said, Let the waters bring forth abundantly the moving creature that hath life, and fowl that may fly above the earth in the open firmament of heaven.",
            "And God created great whales, and every living creature that moveth, which the waters brought forth abundantly, after their kind, and every winged fowl after his kind: and God saw that it was good.",
            "And God blessed them, saying, Be fruitful, and multiply, and fill the waters in the seas, and let fowl multiply in the earth.",
            "And the evening and the morning were the fifth day.",
            "And God said, Let the earth bring forth the living creature after his kind, cattle, and creeping thing, and beast of the earth after his kind: and it was so.",
            "And God made the beast of the earth after his kind, and cattle after their kind, and every thing that creepeth upon the earth after his kind: and God saw that it was good.",
            "And God said, Let us make man in our image, after our likeness: and let them have dominion over the fish of the sea, and over the fowl of the air, and over the cattle, and over all the earth, and over every creeping thing that creepeth upon the earth.",
            "So God created man in his own image, in the image of God created he him; male and female created he them.",
            "And God blessed them, and God said unto them, Be fruitful, and multiply, and replenish the earth, and subdue it: and have dominion over the fish of the sea, and over the fowl of the air, and over every living thing that moveth upon the earth.",
            "And God said, Behold, I have given you every herb bearing seed, which is upon the face of all the earth, and every tree, in the which is the fruit of a tree yielding seed; to you it shall be for meat.",
            "And to every beast of the earth, and to every fowl of the air, and to every thing that creepeth upon the earth, wherein there is life, I have given every green herb for meat: and it was so.",
            "And God saw every thing that he had made, and, behold, it was very good. And the evening and the morning were the sixth day."
        };

        for (int i = 0; i < genesis1.Length; i++)
        {
            var text = genesis1[i].Replace("'", "''");
            sql.AppendLine($"INSERT OR IGNORE INTO Verses (BookId, Chapter, Verse, Text, TranslationId) VALUES (1, 1, {i + 1}, '{text}', 'KJV');");
        }

        // Psalms 23:1-6 (Book ID 19)
        var psalm23 = new[]
        {
            "The LORD is my shepherd; I shall not want.",
            "He maketh me to lie down in green pastures: he leadeth me beside the still waters.",
            "He restoreth my soul: he leadeth me in the paths of righteousness for his name's sake.",
            "Yea, though I walk through the valley of the shadow of death, I will fear no evil: for thou art with me; thy rod and thy staff they comfort me.",
            "Thou preparest a table before me in the presence of mine enemies: thou anointest my head with oil; my cup runneth over.",
            "Surely goodness and mercy shall follow me all the days of my life: and I will dwell in the house of the LORD for ever."
        };

        for (int i = 0; i < psalm23.Length; i++)
        {
            var text = psalm23[i].Replace("'", "''");
            sql.AppendLine($"INSERT OR IGNORE INTO Verses (BookId, Chapter, Verse, Text, TranslationId) VALUES (19, 23, {i + 1}, '{text}', 'KJV');");
        }

        // John 3:1-21 (Book ID 43)
        var john3 = new[]
        {
            "There was a man of the Pharisees, named Nicodemus, a ruler of the Jews:",
            "The same came to Jesus by night, and said unto him, Rabbi, we know that thou art a teacher come from God: for no man can do these miracles that thou doest, except God be with him.",
            "Jesus answered and said unto him, Verily, verily, I say unto thee, Except a man be born again, he cannot see the kingdom of God.",
            "Nicodemus saith unto him, How can a man be born when he is old? can he enter the second time into his mother's womb, and be born?",
            "Jesus answered, Verily, verily, I say unto thee, Except a man be born of water and of the Spirit, he cannot enter into the kingdom of God.",
            "That which is born of the flesh is flesh; and that which is born of the Spirit is spirit.",
            "Marvel not that I said unto thee, Ye must be born again.",
            "The wind bloweth where it listeth, and thou hearest the sound thereof, but canst not tell whence it cometh, and whither it goeth: so is every one that is born of the Spirit.",
            "Nicodemus answered and said unto him, How can these things be?",
            "Jesus answered and said unto him, Art thou a master of Israel, and knowest not these things?",
            "Verily, verily, I say unto thee, We speak that we do know, and testify that we have seen; and ye receive not our witness.",
            "If I have told you earthly things, and ye believe not, how shall ye believe, if I tell you of heavenly things?",
            "And no man hath ascended up to heaven, but he that came down from heaven, even the Son of man which is in heaven.",
            "And as Moses lifted up the serpent in the wilderness, even so must the Son of man be lifted up:",
            "That whosoever believeth in him should not perish, but have eternal life.",
            "For God so loved the world, that he gave his only begotten Son, that whosoever believeth in him should not perish, but have everlasting life.",
            "For God sent not his Son into the world to condemn the world; but that the world through him might be saved.",
            "He that believeth on him is not condemned: but he that believeth not is condemned already, because he hath not believed in the name of the only begotten Son of God.",
            "And this is the condemnation, that light is come into the world, and men loved darkness rather than light, because their deeds were evil.",
            "For every one that doeth evil hateth the light, neither cometh to the light, lest his deeds should be reproved.",
            "But he that doeth truth cometh to the light, that his deeds may be made manifest, that they are wrought in God."
        };

        for (int i = 0; i < john3.Length; i++)
        {
            var text = john3[i].Replace("'", "''");
            sql.AppendLine($"INSERT OR IGNORE INTO Verses (BookId, Chapter, Verse, Text, TranslationId) VALUES (43, 3, {i + 1}, '{text}', 'KJV');");
        }

        // Romans 8:1-39 (Book ID 45)
        var romans8 = new[]
        {
            "There is therefore now no condemnation to them which are in Christ Jesus, who walk not after the flesh, but after the Spirit.",
            "For the law of the Spirit of life in Christ Jesus hath made me free from the law of sin and death.",
            "For what the law could not do, in that it was weak through the flesh, God sending his own Son in the likeness of sinful flesh, and for sin, condemned sin in the flesh:",
            "That the righteousness of the law might be fulfilled in us, who walk not after the flesh, but after the Spirit.",
            "For they that are after the flesh do mind the things of the flesh; but they that are after the Spirit the things of the Spirit.",
            "For to be carnally minded is death; but to be spiritually minded is life and peace.",
            "Because the carnal mind is enmity against God: for it is not subject to the law of God, neither indeed can be.",
            "So then they that are in the flesh cannot please God.",
            "But ye are not in the flesh, but in the Spirit, if so be that the Spirit of God dwell in you. Now if any man have not the Spirit of Christ, he is none of his.",
            "And if Christ be in you, the body is dead because of sin; but the Spirit is life because of righteousness.",
            "But if the Spirit of him that raised up Jesus from the dead dwell in you, he that raised up Christ from the dead shall also quicken your mortal bodies by his Spirit that dwelleth in you.",
            "Therefore, brethren, we are debtors, not to the flesh, to live after the flesh.",
            "For if ye live after the flesh, ye shall die: but if ye through the Spirit do mortify the deeds of the body, ye shall live.",
            "For as many as are led by the Spirit of God, they are the sons of God.",
            "For ye have not received the spirit of bondage again to fear; but ye have received the Spirit of adoption, whereby we cry, Abba, Father.",
            "The Spirit itself beareth witness with our spirit, that we are the children of God:",
            "And if children, then heirs; heirs of God, and joint-heirs with Christ; if so be that we suffer with him, that we may be also glorified together.",
            "For I reckon that the sufferings of this present time are not worthy to be compared with the glory which shall be revealed in us.",
            "For the earnest expectation of the creature waiteth for the manifestation of the sons of God.",
            "For the creature was made subject to vanity, not willingly, but by reason of him who hath subjected the same in hope,",
            "Because the creature itself also shall be delivered from the bondage of corruption into the glorious liberty of the children of God.",
            "For we know that the whole creation groaneth and travaileth in pain together until now.",
            "And not only they, but ourselves also, which have the firstfruits of the Spirit, even we ourselves groan within ourselves, waiting for the adoption, to wit, the redemption of our body.",
            "For we are saved by hope: but hope that is seen is not hope: for what a man seeth, why doth he yet hope for?",
            "But if we hope for that we see not, then do we with patience wait for it.",
            "Likewise the Spirit also helpeth our infirmities: for we know not what we should pray for as we ought: but the Spirit itself maketh intercession for us with groanings which cannot be uttered.",
            "And he that searcheth the hearts knoweth what is the mind of the Spirit, because he maketh intercession for the saints according to the will of God.",
            "And we know that all things work together for good to them that love God, to them who are the called according to his purpose.",
            "For whom he did foreknow, he also did predestinate to be conformed to the image of his Son, that he might be the firstborn among many brethren.",
            "Moreover whom he did predestinate, them he also called: and whom he called, them he also justified: and whom he justified, them he also glorified.",
            "What shall we then say to these things? If God be for us, who can be against us?",
            "He that spared not his own Son, but delivered him up for us all, how shall he not with him also freely give us all things?",
            "Who shall lay any thing to the charge of God's elect? It is God that justifieth.",
            "Who is he that condemneth? It is Christ that died, yea rather, that is risen again, who is even at the right hand of God, who also maketh intercession for us.",
            "Who shall separate us from the love of Christ? shall tribulation, or distress, or persecution, or famine, or nakedness, or peril, or sword?",
            "As it is written, For thy sake we are killed all the day long; we are accounted as sheep for the slaughter.",
            "Nay, in all these things we are more than conquerors through him that loved us.",
            "For I am persuaded, that neither death, nor life, nor angels, nor principalities, nor powers, nor things present, nor things to come,",
            "Nor height, nor depth, nor any other creature, shall be able to separate us from the love of God, which is in Christ Jesus our Lord."
        };

        for (int i = 0; i < romans8.Length; i++)
        {
            var text = romans8[i].Replace("'", "''");
            sql.AppendLine($"INSERT OR IGNORE INTO Verses (BookId, Chapter, Verse, Text, TranslationId) VALUES (45, 8, {i + 1}, '{text}', 'KJV');");
        }

        return sql.ToString();
    }

    /// <summary>
    /// Get the canonical book data (66 books in order).
    /// Returns tuples of (Id, Name, Abbreviation, Testament, ChapterCount).
    /// </summary>
    public static List<(int Id, string Name, string Abbreviation, string Testament, int ChapterCount)> GetBookData()
    {
        return new List<(int, string, string, string, int)>
        {
            // Old Testament
            (1, "Genesis", "Gen", "OldTestament", 50),
            (2, "Exodus", "Exod", "OldTestament", 40),
            (3, "Leviticus", "Lev", "OldTestament", 27),
            (4, "Numbers", "Num", "OldTestament", 36),
            (5, "Deuteronomy", "Deut", "OldTestament", 34),
            (6, "Joshua", "Josh", "OldTestament", 24),
            (7, "Judges", "Judg", "OldTestament", 21),
            (8, "Ruth", "Ruth", "OldTestament", 4),
            (9, "1 Samuel", "1Sam", "OldTestament", 31),
            (10, "2 Samuel", "2Sam", "OldTestament", 24),
            (11, "1 Kings", "1Kgs", "OldTestament", 22),
            (12, "2 Kings", "2Kgs", "OldTestament", 25),
            (13, "1 Chronicles", "1Chr", "OldTestament", 29),
            (14, "2 Chronicles", "2Chr", "OldTestament", 36),
            (15, "Ezra", "Ezra", "OldTestament", 10),
            (16, "Nehemiah", "Neh", "OldTestament", 13),
            (17, "Esther", "Est", "OldTestament", 10),
            (18, "Job", "Job", "OldTestament", 42),
            (19, "Psalms", "Ps", "OldTestament", 150),
            (20, "Proverbs", "Prov", "OldTestament", 31),
            (21, "Ecclesiastes", "Eccl", "OldTestament", 12),
            (22, "Song of Solomon", "Song", "OldTestament", 8),
            (23, "Isaiah", "Isa", "OldTestament", 66),
            (24, "Jeremiah", "Jer", "OldTestament", 52),
            (25, "Lamentations", "Lam", "OldTestament", 5),
            (26, "Ezekiel", "Ezek", "OldTestament", 48),
            (27, "Daniel", "Dan", "OldTestament", 12),
            (28, "Hosea", "Hos", "OldTestament", 14),
            (29, "Joel", "Joel", "OldTestament", 3),
            (30, "Amos", "Amos", "OldTestament", 9),
            (31, "Obadiah", "Obad", "OldTestament", 1),
            (32, "Jonah", "Jonah", "OldTestament", 4),
            (33, "Micah", "Mic", "OldTestament", 7),
            (34, "Nahum", "Nah", "OldTestament", 3),
            (35, "Habakkuk", "Hab", "OldTestament", 3),
            (36, "Zephaniah", "Zeph", "OldTestament", 3),
            (37, "Haggai", "Hag", "OldTestament", 2),
            (38, "Zechariah", "Zech", "OldTestament", 14),
            (39, "Malachi", "Mal", "OldTestament", 4),
            // New Testament
            (40, "Matthew", "Matt", "NewTestament", 28),
            (41, "Mark", "Mark", "NewTestament", 16),
            (42, "Luke", "Luke", "NewTestament", 24),
            (43, "John", "John", "NewTestament", 21),
            (44, "Acts", "Acts", "NewTestament", 28),
            (45, "Romans", "Rom", "NewTestament", 16),
            (46, "1 Corinthians", "1Cor", "NewTestament", 16),
            (47, "2 Corinthians", "2Cor", "NewTestament", 13),
            (48, "Galatians", "Gal", "NewTestament", 6),
            (49, "Ephesians", "Eph", "NewTestament", 6),
            (50, "Philippians", "Phil", "NewTestament", 4),
            (51, "Colossians", "Col", "NewTestament", 4),
            (52, "1 Thessalonians", "1Thess", "NewTestament", 5),
            (53, "2 Thessalonians", "2Thess", "NewTestament", 3),
            (54, "1 Timothy", "1Tim", "NewTestament", 6),
            (55, "2 Timothy", "2Tim", "NewTestament", 4),
            (56, "Titus", "Titus", "NewTestament", 3),
            (57, "Philemon", "Phlm", "NewTestament", 1),
            (58, "Hebrews", "Heb", "NewTestament", 13),
            (59, "James", "James", "NewTestament", 5),
            (60, "1 Peter", "1Pet", "NewTestament", 5),
            (61, "2 Peter", "2Pet", "NewTestament", 3),
            (62, "1 John", "1Jn", "NewTestament", 5),
            (63, "2 John", "2Jn", "NewTestament", 1),
            (64, "3 John", "3Jn", "NewTestament", 1),
            (65, "Jude", "Jude", "NewTestament", 1),
            (66, "Revelation", "Rev", "NewTestament", 22)
        };
    }
}
