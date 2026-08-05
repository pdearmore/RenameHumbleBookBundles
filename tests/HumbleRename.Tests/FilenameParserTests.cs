namespace HumbleRename.Tests;

/// <summary>
/// End-to-end naming tests driven by real Humble Bundle filenames.
/// </summary>
/// <remarks>
/// Every case below is an actual file from a Humble comics bundle. Asserting on the
/// final on-disk name (rather than intermediate fields) means these tests fail if any
/// stage of the pipeline regresses — segmentation, casing, token extraction, template
/// rendering or filename sanitisation.
/// </remarks>
public class FilenameParserTests
{
    [Theory]
    // Run-together lowercase, the signature Humble shape.
    [InlineData("30daysofnight", "30 Days of Night")]
    [InlineData("whitesand", "White Sand")]
    [InlineData("imageexposampler", "Image Expo Sampler")]
    [InlineData("faithandthefutureforce", "Faith and the Future Force")]
    [InlineData("chillingadventuresofsabrina_vol1", "Chilling Adventures of Sabrina Vol. 01")]
    // Statistically ambiguous splits that the lexicon has to settle.
    [InlineData("warmother", "War Mother")]
    [InlineData("nowhereman_vol1", "Nowhere Man Vol. 01")]
    // Acronyms must not be title-cased into "Ptsd".
    [InlineData("ptsdradio_vol1_ebook", "PTSD Radio Vol. 01")]
    [InlineData("4001a_d_deluxeedition", "4001 A.D. (Deluxe Edition)")]
    // Volume glued to the name, plus a Humble download id to discard.
    [InlineData("LockeandKeyv1_1414530092", "Locke & Key Vol. 01")]
    [InlineData("FromHell_1409941126", "From Hell")]
    [InlineData("Shutter_vol1_1420484117", "Shutter Vol. 01")]
    // Series year glued to the name.
    [InlineData("quantumandwoody2017_issue1", "Quantum and Woody #1 (2017)")]
    [InlineData("shadowman2018_issue1", "Shadowman #1 (2018)")]
    [InlineData("x-omanowar2017_vol1", "X-O Manowar Vol. 01 (2017)")]
    // Edition markers pulled out of the title.
    [InlineData("harbinger_deluxeedition_book1", "Harbinger Book 1 (Deluxe Edition)")]
    [InlineData("humbleexclusive_armyofdarknessoneshot", "Army of Darkness (Humble Exclusive, One-Shot)")]
    [InlineData("redteam_vol1_season1", "Red Team Vol. 01 (Season One)")]
    [InlineData("atraincalledlovetp", "A Train Called Love (Trade Paperback)")]
    // Multi-segment names become title plus subtitle.
    [InlineData("divinity_thecompletetrilogy_deluxeedition", "Divinity - The Complete Trilogy (Deluxe Edition)")]
    [InlineData("inthedark_ahorroranthology_ebook", "In the Dark - A Horror Anthology")]
    [InlineData("redteam_doubletapcentermass", "Red Team - Double Tap, Center Mass")]
    // Already-spaced names only need casing and token extraction fixed.
    [InlineData("The Call Of The Stars (1978)", "The Call of the Stars (1978)")]
    [InlineData("Transformers vs. The Terminator #1", "Transformers vs. the Terminator #1")]
    [InlineData("Edgar Allen Poe's Snifter of Terror 004", "Edgar Allen Poe's Snifter of Terror #004")]
    // Calibre exports: "Title - Author", sometimes with the author unknown.
    [InlineData("Nailbiter Vol. 1 - Joshua Williamson", "Nailbiter Vol. 01")]
    [InlineData("Satellite Sam, Vol. 1 TP - Matt Fraction", "Satellite Sam Vol. 01 (Trade Paperback)")]
    [InlineData("Star Wars Omnibus Vol 1 - Unknown", "Star Wars Omnibus Vol. 01")]
    // A trailing word that merely repeats the title is dropped.
    [InlineData("MAD Magazine #4 - MAD", "MAD Magazine #4")]
    // Scene-release tags and the release group are discarded, the year is kept.
    [InlineData("Predator - Hunters (2018) (digital) (The Magicians-Empire)", "Predator - Hunters (2018)")]
    public void ProducesExpectedName(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.FinalName(stem));

    [Theory]
    [InlineData("ajin_demihuman_vol1", 1)]
    [InlineData("parasyte_vol2", 2)]
    [InlineData("completebattlefield_vol3", 3)]
    [InlineData("Nailbiter Vol. 1 - Joshua Williamson", 1)]
    public void ExtractsVolume(string stem, int expected) =>
        Assert.Equal(expected, TestEngine.Parse(stem).Volume);

    [Theory]
    [InlineData("undiscoveredcountry_issue1", "1")]
    [InlineData("redsonja_issue0_humbleexclusive", "0")]
    [InlineData("Edgar Allen Poe's Snifter of Terror 004", "004")]
    public void ExtractsIssue(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.Parse(stem).Issue);

    [Theory]
    [InlineData("Nailbiter Vol. 1 - Joshua Williamson", "Joshua Williamson")]
    [InlineData("Saga Book One - Brian K. Vaughan", "Brian K. Vaughan")]
    public void ExtractsAuthor(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.Parse(stem).Author);

    [Fact]
    public void TreatsUnknownAuthorAsAbsent()
    {
        var parsed = TestEngine.Parse("Star Wars Legacy - Unknown");

        Assert.Null(parsed.Author);
        Assert.Equal("Star Wars Legacy", parsed.Title);
    }

    [Fact]
    public void KeepsSingleTrailingWordAsSubtitleNotAuthor()
    {
        // "Hunters" is part of the title; only a plausible personal name is an author.
        var parsed = TestEngine.Parse("Predator - Hunters (2018)");

        Assert.Null(parsed.Author);
        Assert.Equal(2018, parsed.Year);
    }

    [Theory]
    // Calibre clips titles around 30 characters, leaving a dangling fragment.
    [InlineData("Star Wars Omnibus Rise of the S - Unknown")]
    [InlineData("The Action Bible_ God's Redempt - Sergio Cariello")]
    public void FlagsTruncatedTitles(string stem) =>
        Assert.True(TestEngine.Parse(stem).LooksTruncated);

    [Theory]
    [InlineData("britannia")]
    [InlineData("ajin_demihuman_vol1")]
    [InlineData("chillingadventuresofsabrina_vol1")]
    [InlineData("The Call Of The Stars (1978)")]
    // An initialism is a complete title, not a word clipped short.
    [InlineData("thewalkingdead_fcbd")]
    // Supplied verbatim by the lexicon, so complete however odd the last word looks.
    [InlineData("thewalkingdead_heresnegan")]
    public void DoesNotFlagCompleteTitles(string stem) =>
        Assert.False(TestEngine.Parse(stem).LooksTruncated);

    [Fact]
    public void UnderscoreBeforeSpaceIsReadAsColon()
    {
        // Windows forbids ':' in filenames, so exporters write "Title_ Subtitle".
        var parsed = TestEngine.Parse("The Action Bible_ God's Redempt");

        Assert.Equal("The Action Bible", parsed.Series);
        Assert.Equal("God's Redempt", parsed.Subtitle);
    }

    [Fact]
    public void DiscardsHumbleAssetIdButKeepsIsbn()
    {
        Assert.Null(TestEngine.Parse("FromHell_1409941126").Isbn);
        Assert.Equal("9781632152176", TestEngine.Parse("Nailbiter_9781632152176").Isbn);
    }

    [Theory]
    // Humble builds bundles around one author and glues the name onto every file.
    // The orphaned possessive 's' is the trap: a word-frequency splitter reads it as
    // the start of the next word and turns "'s Troll Bridge" into "Stroll Bridge".
    [InlineData("neilgaimanstrollbridge", "Neil Gaiman's Troll Bridge")]
    [InlineData("neilgaimanschivalry", "Neil Gaiman's Chivalry")]
    [InlineData("neilgaimanssnowglassapples", "Neil Gaiman's Snow, Glass, Apples")]
    [InlineData("neilgaimanshowtotalktogirlsatparties", "Neil Gaiman's How to Talk to Girls at Parties")]
    [InlineData("neilgaimansastudyinemerald", "Neil Gaiman's A Study in Emerald")]
    public void ExpandsPossessiveAuthorPrefix(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.FinalName(stem));

    [Theory]
    // The suffix must stay welded to its number, or it splits to "2 Nd Edition".
    [InlineData("murdermysteries2ndedition", "Murder Mysteries (Second Edition)")]
    [InlineData("creaturesofthenightsecondedition", "Creatures of the Night (Second Edition)")]
    public void KeepsOrdinalsIntact(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.FinalName(stem));

    [Theory]
    // A download id run straight onto the title, with no separator to split on.
    [InlineData("anhonestanswerandotherstories1442260106", "An Honest Answer and Other Stories")]
    [InlineData("feedersandeatersandotherstories1442343441", "Feeders and Eaters and Other Stories")]
    public void StripsGluedAssetIds(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.FinalName(stem));

    [Theory]
    // A short vowel-free run is an initialism, not two words to be split apart.
    [InlineData("thewalkingdead_fcbd", "The Walking Dead - FCBD")]
    [InlineData("thewalkingdead_heresnegan", "The Walking Dead - Here's Negan")]
    [InlineData("thewalkingdead_survivorsguide", "The Walking Dead - The Survivors' Guide")]
    [InlineData("thewalkingdead_alloutwarapedition", "The Walking Dead - All Out War (AP Edition)")]
    public void HandlesSpecialEditionsAndInitialisms(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.FinalName(stem));

    [Theory]
    [InlineData("thewalkingdead_vol1", "The Walking Dead Vol. 01")]
    [InlineData("thewalkingdead_vol9", "The Walking Dead Vol. 09")]
    [InlineData("thewalkingdead_vol32", "The Walking Dead Vol. 32")]
    public void PadsVolumeNumbersConsistently(string stem, string expected) =>
        Assert.Equal(expected, TestEngine.FinalName(stem));

    [Fact]
    public void DropsWindowsDuplicateFileSuffix() =>
        Assert.Equal("Signal to Noise", TestEngine.FinalName("signaltonoise (1)"));

    [Fact]
    public void ReadsVolumeOffTheSeriesSegment()
    {
        // "americangodsvolume1_shadows" is American Gods vol 1, subtitled Shadows.
        var parsed = TestEngine.Parse("americangodsvolume1_shadowsgraphicnovel");

        Assert.Equal(1, parsed.Volume);
        Assert.Equal("American Gods", parsed.Series);
        Assert.Equal("Shadows", parsed.Subtitle);
    }

    [Fact]
    public void EmptyInputYieldsNoTitle() =>
        Assert.False(TestEngine.Parse(string.Empty).HasTitle);
}
