#:/bin/sh

cat ForumPost.bb |
sed 's:\[SIZE=4\]\[B\]\(.*\)\[/B\]\[/SIZE\]:## \1:I' | \
sed 's:\[SIZE=3\]\[B\]\(.*\)\[/B\]\[/SIZE\]:#### \1:I' | \
sed 's:^\[\*\]\s*:* :' | \
sed '/\[\/*[Ll][Ii][Ss][Tt]\]/d' | \
sed '/\[\/*[Ss][Pp][Oo][Ii][Ll][Ee][Rr]\(=.*\)*\]/d' | \
sed 's:\[\/*CODE\]:~~~~:I' | \
sed 's:\[b\]\(.*\)\[/b\]:**\1**:I' | \
sed 's:\[i\]\(.*\)\[/i\]:_\1_:I' | \
sed 's:\[s\]\(.*\)\[/s\]:~~\1~~:I' | \
sed 's:\[URL=\"\(.*\)\"\]\(.*\)\[/URL\]:[\2]\(\1\):I' | \
sed 's:\[IMG\]\(.*\)\[/IMG\]:\![image]\(\1\):I' > README.md
