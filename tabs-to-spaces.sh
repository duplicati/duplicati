find Duplicati -type f -name *.html -exec sed -i '' $'s/\t/    /g' {} +
find Duplicati -type f -name *.js -exec sed -i '' $'s/\t/    /g' {} +
find Duplicati -type f -name *.css -exec sed -i '' $'s/\t/    /g' {} +
find Duplicati -type f -name *.cs -exec sed -i '' $'s/\t/    /g' {} +
find Duplicati -type f -name *.txt -exec sed -i '' $'s/\t/    /g' {} +

find Duplicati -type f -name *.cs | xargs python unix2dos.py
