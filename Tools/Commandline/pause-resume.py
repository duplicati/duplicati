#!/usr/bin/env python3

# Example pause/resume script
# written by will@sowerbutts.com

import requests
import urllib.parse
import sys


class DuplicatiServer(object):
    def __init__(self, base_url):
        self.base_url = base_url
        self.cookiejar = requests.cookies.RequestsCookieJar()
        self.headers = dict()

    def fetch(self, path, post=False, data=None):
        for attempt in range(2):  # we may get a "Missing XSRF token" error on the first attempt
            if post:
                r = requests.post(self.base_url + path, cookies=self.cookiejar, headers=self.headers, data=data)
            else:
                r = requests.get(self.base_url + path, cookies=self.cookiejar, headers=self.headers, data=data)
            self.cookiejar.update(r.cookies)
            self.headers['X-XSRF-Token'] = urllib.parse.unquote(r.cookies['xsrf-token'])
            if r.status_code != 400:
                break
        return r

    def pause(self):
        return self.fetch('api/v1/serverstate/pause', post=True, data=b'')

    def resume(self):
        return self.fetch('api/v1/serverstate/resume', post=True, data=b'')


if __name__ == '__main__':
    ds = DuplicatiServer("http://localhost:8200/")
    r = None
    try:
        cmd = sys.argv[1]
    except IndexError:
        cmd = None
    if cmd == 'pause':
        r = ds.pause()
    elif cmd == 'resume':
        r = ds.resume()
    else:
        print("Syntax: %s [pause|resume]" % sys.argv[0])
        sys.exit(1)
    if r and r.status_code == 200:
        print("OK")
    else:
        print("Something went wrong -- %d %s" % (r.status_code, r.reason))
