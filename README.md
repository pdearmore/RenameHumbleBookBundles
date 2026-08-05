# RenameHumbleBookBundles

Windows C# command line executable to give Humble book bundle files better filenames.
Builds to a single self-contained `HumbleRenamer.exe`.

Humble's DRM-free downloads arrive lowercased and run together, with download ids and
export artefacts glued on. This turns them back into something readable — and into
something Komga, Kavita, ComicRack and Calibre can actually match.

```
  chillingadventuresofsabrina_vol1.cbz    ->  Chilling Adventures of Sabrina Vol. 01.cbz
  x-omanowar2017_vol1.cbz                 ->  X-O Manowar Vol. 01 (2017).cbz
  LockeandKeyv1_1414530092.cbz            ->  Locke & Key Vol. 01.cbz
  warmother.cbz                           ->  War Mother.cbz
  ptsdradio_vol1_ebook.cbz                ->  PTSD Radio Vol. 01.cbz
  4001a_d_deluxeedition.cbz               ->  4001 A.D. (Deluxe Edition).cbz
  humbleexclusive_armyofdarknessoneshot.cbz -> Army of Darkness (Humble Exclusive, One-Shot).cbz
  Satellite Sam, Vol. 1 TP - Matt Fraction.mobi -> Satellite Sam Vol. 01 (2014) (Trade Paperback).mobi
  Predator - Hunters (2018) (digital) (The Magicians-Empire).cbr -> Predator - Hunters (2018).cbr
```

Nothing is renamed until you have seen the complete before/after list and said yes,
and every run can be undone.

## Getting it

Build a standalone `HumbleRenamer.exe` — no .NET install needed to run the result:

```powershell
.\build.ps1
```

The executable lands in `.\publish\HumbleRenamer.exe`. Requires the .NET 10 SDK to build.

## Using it

Double-click `HumbleRenamer.exe`, or run it with no arguments. Everything is chosen from
on-screen menus — there is nothing to memorise.

```
  ▓▒░ Main Menu ░▒▓───────────────────────────────────────────────

   [1]  Folder .................. D:\Comics\Humble Bundle
   [2]  Name format ............. Full descriptive title
   [3]  Include subfolders ...... No
   [4]  Online lookup ........... No
   [5]  Read file metadata ...... Yes
   [6]  File types .............. Comics and ebooks
   [7]  Download cloud files .... No
   [8]  Comic Vine key .......... Not set

   [S]  Scan and preview
   [U]  Undo the last run in this folder
   [Q]  Quit

   Comic Vine key not set - comics match far better with one, and it's free.
   Add one under [8], or get it at https://comicvine.gamespot.com/api/

  ▓▒░ Choose ░▒▓ >
```

Entry `[8]` is where you paste a Comic Vine key, or press `G` to open the free signup
page in your browser. The nudge and `[8]`'s "Not set" both vanish once a key is present —
either pasted here, or found in `HUMBLERENAMER_COMICVINE_KEY`.

Every menu choice — the folder, name format, toggles, file types — is remembered between
runs, so the tool opens where you left off. The settings live in
`%LOCALAPPDATA%\HumbleRenamer\settings.json` (the Comic Vine key is kept separately and
encrypted); a folder passed on the command line still wins over the remembered one.

Drag a folder onto the exe to pre-fill entry `[1]`.

Press `S` and it lists every file's current and proposed name, then offers three ways
forward: save every rename, step through them by hand, or go back and change nothing.
Once applied it immediately offers to put everything back.

### Hand-review

Choosing hand-review walks the files one at a time. For each, it shows every name it
derived — from the filename, from the file's own metadata, from an online catalogue —
and you pick one, type your own, or keep the current name, then it moves to the next.

```
  ▓▒░ Hand-Review  31 of 59 ░▒▓──────────────────────────────────────

   klikklikboom.pdf

   > [1]  Klik Klik Boom - Cover - New.pdf   from file metadata
     [2]  Klik Klik Boom.pdf                 from filename
     [3]  klikklikboom.pdf                   keep current name

   [O]  Open the file in its default app
   [L]  Look it up in the online catalogues
   [E]  Type my own name
   [S]  Skip - keep the current name
   [Q]  Finish review now

  ▓▒░ Choose ░▒▓ >
```

Move with the arrow keys: Up and Down move the highlight through the current file's
options; Left and Right (or Enter for next) move between files, keeping whatever option
is highlighted. A number also picks an option and moves on. Every file keeps its choice
as you go back and forth, so nothing is lost, and `Q` finishes from wherever you are.

Not sure what a file actually is? `O` opens it in whatever viewer the OS uses for that
type — the PDF reader, the CBZ viewer — and leaves you on the same file to decide.

`L` queries the catalogues for that one file on the spot and adds the best match as a
new option, so you can reach for the network only on the handful you're unsure about
rather than turning on lookup for the whole folder. It uses a Comic Vine key if you have
one set, otherwise Open Library and Google Books, and says so if nothing confident comes
back. (Unlike a batch `--online` run, this ignores the "is the local guess weak?" gate —
you asked, so it always looks.)

The highlighted `>` line is what a plain scan would have chosen; press Enter to take it.
Between them, `L` and typing your own are how you overrule a bad guess — a production
artefact an exporter left in the metadata, or a title the splitter got wrong — without
teaching the lexicon anything. Every choice is still applied as one batch with a single
undo, so `[U]` and the revert offer put the whole set back at once.

### Name formats

Chosen from menu entry `[2]`, each shown with a worked example:

| | Format | Result |
| --- | --- | --- |
| `1` | Full descriptive title | `The Walking Dead Vol. 01 - Days Gone Bye (2003).cbz` |
| `2` | Scraper friendly (Komga, Kavita) | `The Walking Dead v01 (2003).cbz` |
| `3` | Books — title and author | `Dune - Frank Herbert (1965).epub` |
| `4` | Just fix casing and spacing | `The Walking Dead.cbz` |
| `C` | Custom template | see [Templates](#templates) |

Use `3` for Humble *book* bundles — it keeps the author, which the comic formats drop.

### File types

Menu entry `[6]`: comics and ebooks together (the default), comics only, ebooks only,
every file regardless of extension, or your own comma-separated list.

## Scripting it

The menus are the point, but every setting is also a switch, so the same binary can be
driven from a script or scheduled task. Passing **any** switch skips the menus entirely.

```powershell
HumbleRenamer D:\Comics --recurse --online
HumbleRenamer D:\Comics --dry-run              # preview and stop
HumbleRenamer D:\Comics --undo                 # put the last run back
HumbleRenamer D:\Books --template "{Title}[ - {Author}][ ({Year})]" --yes
```

### Options

| Option | What it does |
| --- | --- |
| `-t`, `--template <fmt>` | Name layout. See [Templates](#templates). |
| `-r`, `--recurse` | Include subfolders. |
| `-o`, `--online` | Consult online catalogues for missing or clipped titles. |
| `--confidence <n>` | Minimum match confidence, 0–1. Default `0.72`. |
| `--comicvine-key <k>` | Comic Vine API key. |
| `--google-key <k>` | Google Books API key. |
| `--no-metadata` | Do not read metadata from inside files. |
| `--hydrate` | Download cloud-only files so their metadata can be read. |
| `-e`, `--ext <list>` | Extensions to include, e.g. `--ext cbz,cbr,pdf`. |
| `--all-files` | Consider every file, whatever its extension. |
| `--lexicon <path>` | Extra title lexicon to merge in. |
| `-y`, `--yes` | Apply without asking. For scripts. |
| `-n`, `--dry-run` | Show the preview and stop. |
| `-u`, `--undo` | Revert the last run in this folder. |

## How it works

Evidence is layered cheapest-first, and the most trustworthy source wins.

**1. The filename.** Always produces a guess. Download ids (`_1414530092`), scene tags
(`(digital)`, `(The Magicians-Empire)`) and export noise (`_ebook`) are stripped;
volume, issue, book, year and edition markers are pulled out; and run-together text is
split back into words.

Splitting uses [Norvig's Viterbi segmentation](https://norvig.com/ngrams/) over an
embedded 80,000-word frequency corpus. Pure word frequency is not always enough —
`warmother` scores better as *warm other* than *war mother*, because "other" is far
more common than "mother" — so a curated lexicon of comic titles and proper nouns
overrides the statistics where they go wrong.

Casing is headline style: small words stay lowercase mid-title, acronyms are shouted
(`PTSD Radio`), compounds capitalise both halves (`Demi-Human`), and roman numerals are
detected but only when the letters do not also spell a real word — otherwise `mix`
would come out as `MIX`.

**2. Metadata inside the file.** `ComicInfo.xml` from CBZ/CBR/CB7, EXTH records from
MOBI/AZW3, the OPF package from EPUB, and the document information dictionary or XMP
packet from PDF. Formats are identified by their leading bytes, not their extension,
because plenty of files named `.cbr` are really ZIPs.

An embedded title is itself re-parsed, since publishers write things like
`Nailbiter Vol. 1` into the title field and that still needs splitting into a series
and a volume. Structural details the metadata lacks (usually the volume number) are
taken from the filename — but title *fragments* never are, because those are exactly
what a truncating exporter mangled.

Metadata is not trusted blindly. PDFs in particular ship production artefacts in the
title field — one Humble file's `/Title` is literally `Print`, and another reads
`Neverwhere AHE Final Text` on a file that is actually *Neverwear*. An embedded title
that bears no resemblance to the filename is reported and ignored rather than applied,
since renaming a correctly named file after a different book is the worst outcome here.

**3. Online catalogues**, only with `--online`, and only when the local evidence is
weak — a title that looks cut off, no title at all, or a known ISBN. Comic Vine is
tried first when a key is present, then Open Library, then Google Books. A candidate
must clear a confidence floor or it is discarded: leaving a filename-derived guess in
place is much better than confidently applying a wrong title to a hundred files.

Truncation is the case this exists for. Calibre clips titles to roughly 30 characters,
so `Star Wars Omnibus Rise of the S` is scored against candidates by prefix rather
than word overlap, which is what lets it recover `...Rise of the Sith`.

### API keys

Both keyless providers work without configuration, but Google Books throttles by
address and Comic Vine needs a free key. Either can be supplied by flag or environment:

```powershell
$env:HUMBLERENAMER_COMICVINE_KEY    = 'your-key'   # much better for comics specifically
$env:HUMBLERENAMER_GOOGLE_BOOKS_KEY = 'your-key'
```

A Comic Vine key is free — create an account at <https://comicvine.gamespot.com/api/> and
it is shown on that page. In the menus, entry `[8]` opens the signup page for you and lets
you paste the key, so you don't have to set the environment variable at all. A pasted key
is remembered between runs: it is encrypted for your Windows account (DPAPI) and stored at
`%LOCALAPPDATA%\HumbleRenamer\comicvine.key` — never in plaintext, never in the app folder.
`[8]` also clears it. The main menu nags gently until a key is found either way.

Responses are cached in `%LOCALAPPDATA%\HumbleRenamer\lookup-cache.json`, including
misses, so a second run over the same folder does not re-ask.

## Templates

```
{Series}[ Vol. {Volume:00}][ Book {Book}][ #{Issue}][ - {Subtitle}][ ({Year})][ ({Editions})]
```

Tokens: `Series` `Title` `Subtitle` `Volume` `Issue` `Book` `Year` `Author`
`Publisher` `Editions`. A numeric format may follow a colon — `{Volume:00}`.

A `[bracketed]` section disappears entirely when any token inside it is empty, which
is what lets one template serve files with a volume, files with an issue, and files
with neither.

```powershell
HumbleRenamer D:\Comics --template "{Series}[ v{Volume:00}][ ({Year})]"
HumbleRenamer D:\Comics --template "{Author} - {Series}[ ({Year})]"
```

## Teaching it new titles

When a title is guessed wrong, correct it in `%APPDATA%\HumbleRenamer\lexicon.txt` rather
than editing the source. Entries there are merged over the built-in lexicon.

```ini
[titles]
# key = the title with spaces and punctuation removed, lowercased
mynewseries = My New Series
xomanowar = X-O Manowar

[authors]
# lets the parser peel a name off the front of every file in an author bundle,
# including the possessive: "neilgaimanstrollbridge" -> Neil Gaiman's Troll Bridge
neilgaiman = Neil Gaiman

[words]
# proper nouns to teach the word splitter
manowar

[uppercase]
# render these in caps
bprd

[junk]
# tokens to discard wholesale
mybundlename
```

See [`src/HumbleRename/Data/lexicon.txt`](src/HumbleRename/Data/lexicon.txt) for the
full built-in set.

## Undoing

Applying writes a hidden `.humblerenamer-undo.json` into the folder. The tool offers to
revert immediately after a run, and `--undo` works later:

```powershell
HumbleRenamer "D:\Comics\Humble Bundle" --undo
```

## Supported formats

`.cbz` `.cbr` `.cb7` `.cbt` `.pdf` `.epub` `.mobi` `.azw3` `.zip` `.rar`

Use `--ext` to narrow that list or `--all-files` to ignore it.

Cloud-only OneDrive and Dropbox placeholder files are detected and their metadata is
skipped rather than triggering a multi-gigabyte download; pass `--hydrate` if you want
them fetched. Their names are still fixed either way.

## Development

```powershell
dotnet test                    # 124 tests
dotnet run --project src\HumbleRename -- D:\Comics --dry-run
.\build.ps1 -SkipTests
```

Layout:

| Path | Contents |
| --- | --- |
| `src/HumbleRename/Naming` | Word segmentation, title casing, filename parsing |
| `src/HumbleRename/Metadata` | Format sniffing and per-format metadata readers |
| `src/HumbleRename/Lookup` | Catalogue providers, match scoring, response cache |
| `src/HumbleRename/Renaming` | Templates, path safety, planning, apply/undo |
| `src/HumbleRename/Cli` | Argument parsing and terminal output |
| `src/HumbleRename/Data` | Embedded word corpus and lexicon |

## Licence

Public domain — see [LICENSE](LICENSE).
