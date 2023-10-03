import json
import re
import polib
import itertools
import html
from typing import Tuple, Optional

input_messages_file = "messages.json"

output_json : Optional[str] = "messages.{}.json"
output_ts : Optional[str] = "src/locales/messages.{}.ts"
input_po = "../../../../Localizations/webroot/localization_webroot.pot"

print_verbose = True
# replace missing translations with English base, otherwise every missing
# translation prints warnings in browser console
fill_missing = False

#locales = ["bn", "ca", "cs", "da", "de", "en_GB"]
locales = ["de"]

# substitutions to apply to msg to correct for slight mismatches
# pattern, replace, reverse pattern, reverse replace
substitutions = [(re.compile(r'^ (.+)$'), r'\1', re.compile(r'^(.*)$'), r' \1'),
  (re.compile(r'^ (.+) $'), r'\1', re.compile(r'^(.*)$'), r' \1 '),
  (re.compile(r'^(.+) $'), r'\1', re.compile(r'^(.*)$'), r'\1 '),
  (re.compile(r'^(.+) >$'), r'\1', re.compile(r'^(.*)$'), r'\1 >'),
  (re.compile(r'^< (.+)$'), r'\1', re.compile(r'^(.*)$'), r'< \1'),
  (re.compile(r'Generate IAM access policy '), r'Generate IAM\n        access policy', re.compile(r'^(.*)$'), r'\1'),]

# substitutions to apply to pofile entries
substitutions_po = [lambda v,orig: re.sub(r'(.*)\{\{message\}\}', r'\1', orig),
  lambda v,orig: re.sub(r'(\s|\n)+', ' ', v),
  lambda v,orig: re.sub(r"'([^']+)'\s*\|\s*translate", '\1', orig),
  lambda v,orig: re.sub(r'(\{\{[^}]+\}\})', r'', orig),
  lambda v,orig: re.sub(r'\s+', r' ', v),
  lambda v,orig: re.sub(r'\\?\\n\s*', r' ', v),
  lambda v,orig: re.sub(r'(\n\s*)+', r'\n', v),
  lambda v,orig: html.unescape(v)]

# cache for id => (msgid, pattern, subst), used for multiple locales
id_dict = dict()

def replace_placeholders(msg: str, msgid: str, msgstr: str):
  # - find placeholders in msgid
  # - get matching placeholder in msg
  # - replace placeholder from msgid with placeholder from msg in msgstr

  placeholder = r'\{\{[^}]+\}\}'
  start_tag = r'<[^/>][^>]*>'
  end_tag = r'</[^>]+>'
  placeholder_msg = r'\{\$[^}]+\}'
  start_tag_msg = r'\{\$START_\w+\}'
  end_tag_msg = '\{\$CLOSE_\w+\}'

  re_id = re.compile(f'({start_tag})|({end_tag})|({placeholder})')
  re_msg = re.compile(f'({start_tag_msg})|({end_tag_msg})|({placeholder_msg})')

  start_msg = 0
  start_id = 0
  while start_msg < len(msg) and start_id < len(msgid):
    m_id = re_id.search(msgid, start_id)
    
    if m_id is None:
      break

    m_msg = re_msg.search(msg, start_msg)
    if m_msg is None:
      break

    start_id = m_id.end(0)
    start_msg = m_msg.end(0)

    msgstr = msgstr.replace(m_id.group(0), m_msg.group(0))
    

  return msgstr

def fuzzy_search_msgid(pofile: polib.POFile, msg: str) -> Optional[Tuple[str, Optional[re.Pattern],Optional[str]]]:
  po_entry = pofile.find(msg)
  if po_entry is not None:
    return (po_entry.msgid, None, None)

  if '{' not in msg:
    for pattern,repl,r_pattern,r_repl in substitutions:
      if re.match(pattern, msg) is not None:
        po_entry = pofile.find(re.sub(pattern, repl, msg))
        if po_entry is not None:
          return (po_entry.msgid, r_pattern, r_repl)

  # Replace substitutions
  msg_subst = re.sub(r'\{\$START_\w+\}', '{s}', msg)
  msg_subst = re.sub(r'\{\$CLOSE_\w+\}', '{c}', msg_subst)
  msg_subst = re.sub(r'\{\$[^}]+\}', '{}', msg_subst)
  for po_entry in pofile:
    po_subst = re.sub(r'\{\{[^}]+\}\}', '{}', po_entry.msgid)
    po_subst = re.sub(r'<[^/>][^>]*>', '{s}', po_subst)
    po_subst = re.sub(r'</[^>]+>', '{c}', po_subst)
    subst_alternatives = set([po_subst,
                              *(s(po_subst, po_entry.msgid) for s in substitutions_po)])
    for po_subst in subst_alternatives:
      if msg_subst == po_subst:
        return (po_entry.msgid, None, None)
      for pattern,repl,r_pattern,r_repl in substitutions:
        if re.search(pattern, msg_subst) is not None:
          msg_subst2 = re.sub(pattern, repl, msg_subst)
          if po_subst == msg_subst2:
            return (po_entry.msgid, r_pattern, r_repl)

  return None

def merge_locale(locale):
  output_messages_json = None
  if output_json is not None:
    output_messages_json = output_json.format(locale)
  if output_ts is not None:
    output_messages_ts = output_ts.format(locale)
  if output_ts is None and output_json is None:
    raise ValueError('Have to specify either output_ts, output_json or both')
  print('Merging locale:', locale)
  pofile = polib.pofile(input_po.format(locale))

  output_translations = dict()
  output_messages = {
    'locale': locale,
    'translations': output_translations
    }

  missing_items = []

  used_entries = set()

  for id,msg in input_translations.items():
    res = id_dict.get(id, None)
    if res is None:
      res = fuzzy_search_msgid(pofile, msg)
    if res is None:
      missing_items.append((id,msg))
    else:
      id_dict[id] = res
      # unpack tuple
      po_msgid, r_pattern, r_repl = res
      po_entry = pofile.find(po_msgid)
      if po_entry is None:
        missing_items.append((id,msg))
      else:
        used_entries.add(po_entry)
        po_msgstr = po_entry.msgstr
        if r_pattern is not None:
          po_msgid = r_pattern.sub(po_msgid, r_repl)
          po_msgstr = r_pattern.sub(po_msgstr, r_repl)
        output_translations[id] = replace_placeholders(msg, po_msgid, po_msgstr)

  print("Number of input translations:", len(pofile))
  print("Number of output translations:", len(output_translations))
  if len(missing_items) > 0:
    print("Missing translations:", len(missing_items))
    if print_verbose:
      print(json.dumps(dict(missing_items), indent=2))
    
  extra_entries = set(pofile).difference(used_entries)
  if print_verbose:
    print("Extra entries: ", json.dumps([e.msgid for e in extra_entries], indent=2))
  else:
    print("Number of extra entries:", len(extra_entries))

  if fill_missing:
    for id,msg in missing_items:
      output_translations[id] = msg

  if output_messages_json is not None:
    with open(output_messages_json, "w", encoding="utf-8") as f:
      json.dump(output_messages, f, ensure_ascii=False, indent=2)

    print("Written to output file:", output_messages_json)

  if output_messages_ts is not None:
    # Convert to ts file
    with open(output_messages_ts, "w", encoding="utf-8") as f:
      locale_upper = locale.upper().replace("-", "_")
      f.write(f"export const MESSAGES_{locale_upper}: Record<string,string> = ")
      json.dump(output_translations, f, ensure_ascii=False, indent=2)
      f.write(";\n")
  
    print("Written to ts file:", output_messages_ts)
    print()


with open(input_messages_file, "r", encoding="utf-8") as f:
  input_messages = json.load(f)

input_translations = input_messages["translations"]

#pofile = polib.POFile()
#pofile.append(polib.POEntry(msgid="The path does not end with a '{{dirsep}}' character, which means that you include a file, not a folder.\n"
#  "\n"
#  "Do you want to include the specified file?"))
#fuzzy_search_msgid(pofile, "The path does not end with a '{$PH}' character, which means that you include a file, not a folder.\nDo you want to include the specified file?")

for locale in locales:
  merge_locale(locale)
