import re

with open(r"c:\Editor Test\cavedude0.6.0-DR3.sql", "r", encoding="utf-8", errors="replace") as f:
    raw = f.read()

patterns = {
    "TYPE=MyISAM": r"TYPE=MyISAM",
    "TYPE=InnoDB": r"TYPE=InnoDB",
    "TYPE=HEAP": r"TYPE=HEAP",
    "LOCK TABLES": r"LOCK TABLES",
    "UNLOCK TABLES": r"UNLOCK TABLES",
    "auto_increment": r"(?i)auto_increment",
    "unsigned": r"(?i)\bunsigned\b",
    "mediumint": r"(?i)\bmediumint\b",
    "tinyint": r"(?i)\btinyint\b",
    "smallint": r"(?i)\bsmallint\b",
    "bigint": r"(?i)\bbigint\b",
    "int(": r"(?i)\bint\(",
    "enum(": r"(?i)\benum\(",
    "set(": r"(?i)\bset\(",
    "DROP TABLE": r"(?i)DROP TABLE",
    "non-primary KEY": r"(?im)^\s+KEY\s",
    "UNIQUE KEY": r"(?i)UNIQUE KEY",
    "PACK_KEYS": r"PACK_KEYS",
    "COMMENT=": r"(?i)COMMENT='",
    "default ''": r"(?i)default '",
    "backtick `": r"`",
    "double-space PK": r"PRIMARY KEY\s\s\(",
    "/*!": r"/\*!",
}

for name, pat in patterns.items():
    count = len(re.findall(pat, raw))
    if count > 0:
        print(f"{name}: {count}")

# Show some CREATE TABLE examples with non-standard KEY lines
print("\n--- Sample non-primary KEY lines ---")
for m in re.finditer(r"(?im)^\s+KEY\s.*$", raw):
    print(m.group().strip())
    if m.start() > 5000:
        break

# Show sample enum usage
print("\n--- Sample enum lines ---")
for i, m in enumerate(re.finditer(r"(?im)^.*enum\(.*$", raw)):
    print(m.group().strip())
    if i >= 5:
        break

# Show sample auto_increment
print("\n--- Sample auto_increment lines ---")
for i, m in enumerate(re.finditer(r"(?im)^.*auto_increment.*$", raw)):
    print(m.group().strip())
    if i >= 5:
        break

# Show sample LOCK/UNLOCK lines
print("\n--- Sample LOCK/UNLOCK lines ---")
for m in re.finditer(r"(?im)^.*(LOCK TABLES|UNLOCK TABLES).*$", raw):
    print(m.group().strip())
    if m.start() > 10000:
        break
