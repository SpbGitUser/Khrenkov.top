CREATE TABLE Files (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE CHECK (length(Name) > 0),
    Size INTEGER NOT NULL CHECK (Size >= 0),
    ModifiedUtc TEXT NOT NULL
);

CREATE TABLE ShareLinks (
    Token TEXT NOT NULL PRIMARY KEY CHECK (length(Token) > 0),
    FileId INTEGER NOT NULL REFERENCES Files(Id) ON DELETE CASCADE,
    RemainingDownloads INTEGER NOT NULL CHECK (RemainingDownloads >= 0),
    CreatedAtUtc TEXT NOT NULL
);

CREATE INDEX IX_ShareLinks_FileId ON ShareLinks(FileId);
