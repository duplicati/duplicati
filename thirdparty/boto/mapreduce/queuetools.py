#!/usr/bin/python
import socket, sys
from lqs import LQSServer, LQSMessage
import boto
from boto.sqs.jsonmessage import JSONMessage

class LQSClient:

    def __init__(self, host):
        self.host = host
        self.port = LQSServer.PORT
        self.timeout = LQSServer.TIMEOUT
        self.max_len = LQSServer.MAXSIZE
        self.sock = None

    def connect(self):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect((self.host, self.port))

    def decode(self, jsonstr):
        return LQSMessage(jsonvalue=jsonstr)

    def get(self):
        self.sock.send('next')
        try:
            jsonstr = self.sock.recv(self.max_len)
            msg = LQSMessage(jsonvalue=jsonstr)
            return msg
        except:
            print "recv from %s failed" % self.host

    def delete(self, msg):
        self.sock.send('delete %s' % msg['id'])
        try:
            jsonstr = self.sock.recv(self.max_len)
            msg = LQSMessage(jsonvalue=jsonstr)
            return msg
        except:
            print "recv from %s failed" % self.host

    def close(self):
        self.sock.close()

class SQSClient:

    def __init__(self, queue_name):
        self.queue_name = queue_name

    def connect(self):
        self.queue = boto.lookup('sqs', self.queue_name)
        self.queue.set_mesasge_class(JSONMessage)

    def get(self):
        m = self.queue.read()
        return m.get_body()

    def close(self):
        pass

def get_queue(name):
    if name == 'localhost':
        return LQSClient(name)
    else:
        return SQSClient(name)
        
