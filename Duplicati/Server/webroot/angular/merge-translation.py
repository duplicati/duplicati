import json
import re
import polib
import itertools
import html

input_messages_file = "messages.json"

locale = "de"
output_messages_json = f"messages.{locale}.json"
output_messages_ts = f"messages.{locale}.ts"
print_missing = True

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

# pattern, replace, reverse pattern, reverse replace
substitutions = [(re.compile(r'^ (.+)$'), r'\1', re.compile(r'^(.*)$'), r' \1'),
  (re.compile(r'^ (.+) $'), r'\1', re.compile(r'^(.*)$'), r' \1 '),
  (re.compile(r'^(.+) $'), r'\1', re.compile(r'^(.*)$'), r'\1 '),
  (re.compile(r'^(.+) >$'), r'\1', re.compile(r'^(.*)$'), r'\1 >'),
  (re.compile(r'^< (.+)$'), r'\1', re.compile(r'^(.*)$'), r'< \1'),
  (re.compile(r'Generate IAM access policy '), r'Generate IAM\n        access policy', re.compile(r'^(.*)$'), r'\1'),]



def replace_substitutions(msg, msgid, msgstr):
  return msgstr

used_entries = set()

def fuzzy_search(pofile, msg):
  po_entry = pofile.find(msg)
  if po_entry is not None:
    used_entries.add(po_entry)
    return po_entry.msgstr

  if '{' not in msg:
    for pattern,repl,r_pattern,r_repl in substitutions:
      if re.match(pattern, msg) is not None:
        po_entry = pofile.find(re.sub(pattern, repl, msg))
        if po_entry is not None:
          used_entries.add(po_entry)
          return re.sub(r_pattern, r_repl, po_entry.msgstr)

  # Replace substitutions
  msg_subst = re.sub(r'\{((\$PH)|(\$INTERPOLATION))(_[0-9]+)?\}', '{}', msg)
  msg_subst = re.sub(r'\{\$START_\w+\}', '{s}', msg_subst)
  msg_subst = re.sub(r'\{\$CLOSE_\w+\}', '{c}', msg_subst)
  for po_entry in pofile:
    po_subst = re.sub(r'\{\{[^}]+\}\}', '{}', po_entry.msgid)
    po_subst = re.sub(r'<[^/>][^>]*>', '{s}', po_subst)
    po_subst = re.sub(r'</[^>]+>', '{c}', po_subst)
    subst_alternatives = set((po_subst,
                              re.sub(r'(.*)\{\{message\}\}', r'\1', po_entry.msgid),
                              re.sub(r'(\s|\n)+', ' ', po_subst),
                              re.sub(r"'([^']+)'\s*\|\s*translate", '\1', po_entry.msgid),
                              html.unescape(po_subst)))
    for po_subst in subst_alternatives:
      if msg_subst == po_subst:
        used_entries.add(po_entry)
        return replace_substitutions(msg, po_entry.msgid, po_entry.msgstr)
      for pattern,repl,r_pattern,r_repl in substitutions:
        if re.match(pattern, msg_subst) is not None:
          msg_subst2 = re.sub(pattern, repl, msg_subst)
          if po_subst == msg_subst2:
            used_entries.add(po_entry)
            return replace_substitutions(msg, re.sub(r_pattern, r_repl, po_entry.msgid), re.sub(r_pattern, r_repl, po_entry.msgstr))

  return None

for id,msg in input_translations.items():
  res = fuzzy_search(pofile, msg)
  if res is None:
    missing_items.append((id,msg))
  else:
    output_translations[id] = res

print("Number of input translations:", len(pofile))
print("Number of output translations:", len(output_translations))
if len(missing_items) > 0:
  if print_missing:
    print(json.dumps(dict(missing_items), indent=2))
  print("Missing translations:", len(missing_items))

if print_missing:
  extra_entries = set(pofile).difference(used_entries)
  print("Extra entries: ", json.dumps([e.msgid for e in extra_entries], indent=2))

with open(output_messages_json, "w", encoding="utf-8") as f:
  json.dump(output_messages, f, ensure_ascii=False, indent=2)


print("Written to output file:", output_messages_json)

# Convert to ts file
with open(output_messages_ts, "w", encoding="utf-8") as f:
  locale_upper = locale.upper().replace("-", "_")
  f.write(f"export const MESSAGES_{locale_upper}: Record<string,string> = ")
  json.dump(output_translations, f, ensure_ascii=False, indent=2)
  f.write(";\n")
  
print("Written to ts file:", output_messages_ts)

