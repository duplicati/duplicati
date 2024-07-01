#!/usr/bin/env python3

# Example pause/resume script
# written by will@sowerbutts.com

import requests
import sys


class DuplicatiServer(object):
    def __init__(self, base_url, password):
        self.base_url = base_url
        self.headers = dict()
        self.password = password
        self.access_token = None

    def fetch(self, path, post=False, data=None):
        if (self.access_token is None):
            r = requests.post(self.base_url + 'api/v1/auth/login', headers=self.headers, data=b'{"Password":"%s"}' % self.password)
            if r.status_code == 200:
                self.access_token = r.json()['AccessToken']
            else:
                return r
        
        self.headers['Authorization'] = 'Bearer %s' % self.access_token
        if post:
            r = requests.post(self.base_url + path, headers=self.headers, data=data)
        else:
            r = requests.get(self.base_url + path, headers=self.headers, data=data)
        return r

    def pause(self):
        return self.fetch('api/v1/serverstate/pause', post=True, data=b'')

    def resume(self):
        return self.fetch('api/v1/serverstate/resume', post=True, data=b'')


if __name__ == '__main__':
    
    r = None
    password = None
    try:
        cmd = sys.argv[1]
        password = sys.argv[2]
    except IndexError:
        cmd = None

    if not cmd:
        print("Syntax: %s [pause|resume] [password]" % sys.argv[0])
        sys.exit(1)

    ds = DuplicatiServer("http://localhost:8200/", password)

    if cmd == 'pause':
        r = ds.pause()
    elif cmd == 'resume':
        r = ds.resume()

    if r and r.status_code == 200:
        print("OK")
    else:
        print("Something went wrong -- %d %s" % (r.status_code, r.reason))
