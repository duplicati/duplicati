import json
import polib

input_messages_file = "messages.json"

locale = "de"
output_messages_json = f"messages.{locale}.json"
output_messages_ts = f"messages.{locale}.ts"

pofile = polib.pofile(f"../../../../Localizations/webroot/localization_webroot-{locale}.po")

with open(input_messages_file, "r", encoding="utf-8") as f:
  input_messages = json.load(f)

input_translations = input_messages["translations"]
print(len(input_translations))

output_translations = dict()
output_messages = {
  'locale': locale,
  'translations': output_translations
  }

missing_items = []

for id,msg in input_translations.items():
  po_entry = pofile.find(msg)
  if po_entry is None:
    missing_items.append((id,msg))
  else:
    output_translations[id] = po_entry.msgstr

print("Number of input translations:", len(pofile))
print("Number of output translations:", len(output_translations))
if len(missing_items) > 0:
  print("Missing translations:", len(missing_items))

with open(output_messages_json, "w", encoding="utf-8") as f:
  json.dump(output_messages, f, ensure_ascii=False, indent=2)

print("Written to output file:", output_messages_json)

# Convert to ts file
with open(output_messages_ts, "w", encoding="utf-8") as f:
  locale_upper = locale.upper().replace("-", "_")
  f.write(f"export const MESSAGES_{locale_upper}: Record<string,string> = ")
  json.dump(output_translations, f, ensure_ascii=False, indent=2)
  f.write(";\n")
