"""
Convert MySQL 4.0 dump (cavedude0.6.0-DR3.sql) to modern MySQL 8+ compatible SQL.

Changes made:
1. TYPE=MyISAM → ENGINE=InnoDB (TYPE removed in MySQL 5.5)
2. Remove PACK_KEYS=N table option (InnoDB doesn't use it)
3. Remove integer display widths: int(11)→INT, tinyint(3)→TINYINT, etc.
   (deprecated in MySQL 8.0.17+)
4. timestamp(14) → TIMESTAMP (old display-width syntax removed)
5. PRIMARY KEY  ( → PRIMARY KEY ( (cosmetic double-space fix)
6. Backtick-quote `rank` column/key references (RANK is reserved in MySQL 8.0.2+)
"""

import re
import sys

INPUT  = r"c:\Editor Test\cavedude0.6.0-DR3.sql"
OUTPUT = r"c:\Editor Test\cavedude0.6.0-DR3-modern.sql"

# --- helpers ---------------------------------------------------------------

def fix_int_display_widths(line):
    """Remove display widths from integer types: int(11) → INT, etc."""
    # Match integer type names followed by (N) but NOT auto_increment columns
    # that might have special meaning.  Display widths are purely cosmetic.
    return re.sub(
        r'\b(tinyint|smallint|mediumint|int|bigint)\(\d+\)',
        lambda m: m.group(1),
        line,
        flags=re.IGNORECASE
    )

def fix_timestamp_width(line):
    """timestamp(14) → TIMESTAMP"""
    return re.sub(
        r'\btimestamp\(\d+\)',
        'TIMESTAMP',
        line,
        flags=re.IGNORECASE
    )

def fix_type_engine(line):
    """TYPE=MyISAM ... → ENGINE=InnoDB, removing PACK_KEYS and COMMENT"""
    # Patterns seen in the file:
    #   ) TYPE=MyISAM;
    #   ) TYPE=MyISAM PACK_KEYS=0;
    #   ) TYPE=MyISAM COMMENT='...';
    return re.sub(
        r'\)\s*TYPE=MyISAM[^;]*;',
        ') ENGINE=InnoDB;',
        line,
        flags=re.IGNORECASE
    )

def fix_double_space_pk(line):
    """PRIMARY KEY  ( → PRIMARY KEY ("""
    return line.replace('PRIMARY KEY  (', 'PRIMARY KEY (')

def fix_rank_reserved(line):
    """Quote `rank` as identifier since RANK is reserved in MySQL 8.0+"""
    # In column definitions:  "rank tinyint ..."
    line = re.sub(
        r'^(\s+)rank(\s+(?:tinyint|smallint|mediumint|int|bigint)\b)',
        r'\1`rank`\2',
        line,
        flags=re.IGNORECASE
    )
    # In PRIMARY KEY / KEY / UNIQUE KEY references:
    # e.g., PRIMARY KEY (aaid,rank)  or  KEY rank (rank)
    # Replace unquoted rank in parenthesized key column lists
    line = re.sub(
        r'(?<=\()rank(?=\))',       # (rank)
        '`rank`',
        line
    )
    line = re.sub(
        r'(?<=\()rank(?=,)',        # (rank,...
        '`rank`',
        line
    )
    line = re.sub(
        r'(?<=,)rank(?=\))',        # (...,rank)
        '`rank`',
        line
    )
    line = re.sub(
        r'(?<=,)rank(?=,)',         # (...,rank,...
        '`rank`',
        line
    )
    return line


# --- main ------------------------------------------------------------------

def convert():
    print(f"Reading {INPUT} ...")
    with open(INPUT, 'r', encoding='utf-8', errors='replace') as fin:
        lines = fin.readlines()

    print(f"Read {len(lines)} lines.  Converting ...")

    changes = {
        'engine': 0,
        'int_width': 0,
        'timestamp': 0,
        'pk_space': 0,
        'rank_quote': 0,
    }

    out_lines = []
    for line in lines:
        original = line

        # 1. TYPE=MyISAM → ENGINE=InnoDB
        if 'TYPE=MyISAM' in line or 'TYPE=myisam' in line.lower():
            line = fix_type_engine(line)
            if line != original:
                changes['engine'] += 1

        # 2. Integer display widths (only in CREATE TABLE column defs)
        prev = line
        line = fix_int_display_widths(line)
        if line != prev:
            changes['int_width'] += 1

        # 3. timestamp(14)
        prev = line
        line = fix_timestamp_width(line)
        if line != prev:
            changes['timestamp'] += 1

        # 4. Double-space in PRIMARY KEY
        prev = line
        line = fix_double_space_pk(line)
        if line != prev:
            changes['pk_space'] += 1

        # 5. Quote `rank` reserved word
        prev = line
        line = fix_rank_reserved(line)
        if line != prev:
            changes['rank_quote'] += 1

        out_lines.append(line)

    # Update header comment
    header = (
        "-- Converted from MySQL 4.0 dump to MySQL 8.0+ compatible format\n"
        "-- Original: cavedude0.6.0-DR3.sql (MySQL dump 9.11, server 4.0.21)\n"
        "--\n"
    )
    out_lines.insert(0, header)

    print(f"Writing {OUTPUT} ...")
    with open(OUTPUT, 'w', encoding='utf-8') as fout:
        fout.writelines(out_lines)

    print("\nConversion summary:")
    print(f"  TYPE=MyISAM → ENGINE=InnoDB : {changes['engine']}")
    print(f"  Integer display widths removed: {changes['int_width']}")
    print(f"  timestamp(N) → TIMESTAMP     : {changes['timestamp']}")
    print(f"  PRIMARY KEY double-space fix  : {changes['pk_space']}")
    print(f"  rank → `rank` (reserved word) : {changes['rank_quote']}")
    print(f"\nOutput: {OUTPUT}")

if __name__ == '__main__':
    convert()
