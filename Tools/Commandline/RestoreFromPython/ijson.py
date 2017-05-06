# ijson 2.3
# Copyright (c) 2010, Ivan Sagalaev
# All rights reserved.
# Redistribution and use in source and binary forms, with or without
# modification, are permitted provided that the following conditions are met:
#
#     * Redistributions of source code must retain the above copyright
#       notice, this list of conditions and the following disclaimer.
#     * Redistributions in binary form must reproduce the above copyright
#       notice, this list of conditions and the following disclaimer in the
#       documentation and/or other materials provided with the distribution.
#     * Neither the name "ijson" nor the names of its contributors
#       may be used to endorse or promote products derived from this software
#       without specific prior written permission.
#
# This software is provided by the regents and contributors ``as is'' and any
# express or implied warranties, including, but not limited to, the implied
# warranties of merchantability and fitness for a particular purpose are
# disclaimed. in no event shall the regents and contributors be liable for any
# direct, indirect, incidental, special, exemplary, or consequential damages
# (including, but not limited to, procurement of substitute goods or services;
# loss of use, data, or profits; or business interruption) however caused and
# on any theory of liability, whether in contract, strict liability, or tort
# (including negligence or otherwise) arising in any way out of the use of this
# software, even if advised of the possibility of such damage.

from __future__ import unicode_literals
import decimal
import re
import sys
from codecs import getreader

class JSONError(Exception):
    pass  # base exception for all parsing errors.

class IncompleteJSONError(JSONError):
    pass  # raised when the parser can't read expected data from a stream.

class ObjectBuilder(object):
    def __init__(self):
        def initial_set(value):
            self.value = value
        self.containers = [initial_set]

    def event(self, event, value):
        if event == 'map_key':
            self.key = value
        elif event == 'start_map':
            mapval = {}
            self.containers[-1](mapval)
            def setter(value):
                mapval[self.key] = value
            self.containers.append(setter)
        elif event == 'start_array':
            array = []
            self.containers[-1](array)
            self.containers.append(array.append)
        elif event == 'end_array' or event == 'end_map':
            self.containers.pop()
        else:
            self.containers[-1](value)

def parse_impl(basic_events):
    path = []
    for event, value in basic_events:
        if event == 'map_key':
            prefix = '.'.join(path[:-1])
            path[-1] = value
        elif event == 'start_map':
            prefix = '.'.join(path)
            path.append(None)
        elif event == 'end_map':
            path.pop()
            prefix = '.'.join(path)
        elif event == 'start_array':
            prefix = '.'.join(path)
            path.append('item')
        elif event == 'end_array':
            path.pop()
            prefix = '.'.join(path)
        else:  # any scalar value
            prefix = '.'.join(path)

        yield prefix, event, value

def items_impl(prefixed_events, prefix):
    prefixed_events = iter(prefixed_events)
    try:
        while True:
            current, event, value = next(prefixed_events)
            if current == prefix:
                if event in ('start_map', 'start_array'):
                    builder = ObjectBuilder()
                    end_event = event.replace('start', 'end')
                    while (current, event) != (prefix, end_event):
                        builder.event(event, value)
                        current, event, value = next(prefixed_events)
                    yield builder.value
                else:
                    yield value
    except StopIteration:
        pass

def number(str_value):
    number = decimal.Decimal(str_value)
    int_number = int(number)
    if int_number == number:
        number = int_number
    return number

class UnexpectedSymbol(JSONError):
    def __init__(self, symbol, pos):
        super(UnexpectedSymbol, self).__init__(
            'Unexpected symbol %r at %d' % (symbol, pos)
        )

BUFSIZE = 16 * 1024
LEXEME_RE = re.compile(r'[a-z0-9eE\.\+-]+|\S')

def Lexer(f, buf_size=BUFSIZE):
    if isinstance(f.read(0), bytetype):
        f = getreader('utf-8')(f)
    buf = f.read(buf_size)
    pos = 0
    discarded = 0
    while True:
        match = LEXEME_RE.search(buf, pos)
        if match:
            lexeme = match.group()
            if lexeme == '"':
                pos = match.start()
                start = pos + 1
                while True:
                    try:
                        end = buf.index('"', start)
                        escpos = end - 1
                        while buf[escpos] == '\\':
                            escpos -= 1
                        if (end - escpos) % 2 == 0:
                            start = end + 1
                        else:
                            break
                    except ValueError:
                        data = f.read(buf_size)
                        if not data:
                            raise IncompleteJSONError('Incomplete string lexeme')
                        buf += data
                yield discarded + pos, buf[pos:end + 1]
                pos = end + 1
            else:
                while match.end() == len(buf):
                    data = f.read(buf_size)
                    if not data:
                        break
                    buf += data
                    match = LEXEME_RE.search(buf, pos)
                    lexeme = match.group()
                yield discarded + match.start(), lexeme
                pos = match.end()
        else:
            data = f.read(buf_size)
            if not data:
                break
            discarded += len(buf)
            buf = data
            pos = 0

def unescape(s):
    start = 0
    result = ''
    while start < len(s):
        pos = s.find('\\', start)
        if pos == -1:
            if start == 0:
                return s
            result += s[start:]
            break
        result += s[start:pos]
        pos += 1
        esc = s[pos]
        if esc == 'u':
            result += chr(int(s[pos + 1:pos + 5], 16))
            pos += 4
        elif esc == 'b':
            result += '\b'
        elif esc == 'f':
            result += '\f'
        elif esc == 'n':
            result += '\n'
        elif esc == 'r':
            result += '\r'
        elif esc == 't':
            result += '\t'
        else:
            result += esc
        start = pos + 1
    return result

def parse_value(lexer, symbol=None, pos=0):
    try:
        if symbol is None:
            pos, symbol = next(lexer)
        if symbol == 'null':
            yield ('null', None)
        elif symbol == 'true':
            yield ('boolean', True)
        elif symbol == 'false':
            yield ('boolean', False)
        elif symbol == '[':
            for event in parse_array(lexer):
                yield event
        elif symbol == '{':
            for event in parse_object(lexer):
                yield event
        elif symbol[0] == '"':
            yield ('string', unescape(symbol[1:-1]))
        else:
            try:
                yield ('number', number(symbol))
            except decimal.InvalidOperation:
                raise UnexpectedSymbol(symbol, pos)
    except StopIteration:
        raise IncompleteJSONError('Incomplete JSON data')

def parse_array(lexer):
    yield ('start_array', None)
    try:
        pos, symbol = next(lexer)
        if symbol != ']':
            while True:
                for event in parse_value(lexer, symbol, pos):
                    yield event
                pos, symbol = next(lexer)
                if symbol == ']':
                    break
                if symbol != ',':
                    raise UnexpectedSymbol(symbol, pos)
                pos, symbol = next(lexer)
        yield ('end_array', None)
    except StopIteration:
        raise IncompleteJSONError('Incomplete JSON data')

def parse_object(lexer):
    yield ('start_map', None)
    try:
        pos, symbol = next(lexer)
        if symbol != '}':
            while True:
                if symbol[0] != '"':
                    raise UnexpectedSymbol(symbol, pos)
                yield ('map_key', unescape(symbol[1:-1]))
                pos, symbol = next(lexer)
                if symbol != ':':
                    raise UnexpectedSymbol(symbol, pos)
                for event in parse_value(lexer, None, pos):
                    yield event
                pos, symbol = next(lexer)
                if symbol == '}':
                    break
                if symbol != ',':
                    raise UnexpectedSymbol(symbol, pos)
                pos, symbol = next(lexer)
        yield ('end_map', None)
    except StopIteration:
        raise IncompleteJSONError('Incomplete JSON data')

def basic_parse(file, buf_size=BUFSIZE):
    lexer = iter(Lexer(file, buf_size))
    for value in parse_value(lexer):
        yield value
    try:
        next(lexer)
    except StopIteration:
        pass
    else:
        raise JSONError('Additional data')

def parse(file, buf_size=BUFSIZE):
    return parse_impl(basic_parse(file, buf_size=buf_size))

def items(file, prefix):
    return items_impl(parse(file), prefix)

def b2s(b):
    return b.decode('utf-8')

assert sys.version_info[0] >= 3
bytetype = bytes
