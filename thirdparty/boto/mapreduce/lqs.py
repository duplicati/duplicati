import SocketServer, os, datetime, sys, random, time
import simplejson

class LQSCommand:

    def __init__(self, line):
        self.raw_line = line
        self.line = self.raw_line.strip()
        l = self.line.split(' ')
        self.name = l[0]
        if len(l) > 1:
            self.args = [arg for arg in l[1:] if arg]
        else:
            self.args = []

class LQSMessage(dict):

    def __init__(self, item=None, args=None, jsonvalue=None):
        dict.__init__(self)
        if jsonvalue:
            self.decode(jsonvalue)
        else:
            self['id'] = '%d_%d' % (int(time.time()), int(random.random()*1000000))
            self['item'] = item
            self['args'] = args

    def encode(self):
        return simplejson.dumps(self)

    def decode(self, value):
        self.update(simplejson.loads(value))

    def is_empty(self):
        if self['item'] == None:
            return True
        return False

class LQSServer(SocketServer.UDPServer):

    PORT = 5151
    TIMEOUT = 30
    MAXSIZE = 8192

    def __init__(self, server_address, RequestHandlerClass, iterator, args=None):
        server_address = (server_address, self.PORT)
        SocketServer.UDPServer.__init__(self, server_address, RequestHandlerClass)
        self.count = 0
        self.iterator = iterator
        self.args = args
        self.start = datetime.datetime.now()
        self.end = None
        self.extant = []

class LQSHandler(SocketServer.DatagramRequestHandler):

    def get_cmd(self):
        return LQSCommand(self.rfile.readline())

    def build_msg(self):
        if not self.server.iterator:
            return LQSMessage(None)
        try:
            item = self.server.iterator.next()
            msg = LQSMessage(item, self.server.args)
            return msg
        except StopIteration:
            self.server.iterator = None
            return LQSMessage(None)

    def respond(self, msg):
        self.wfile.write(msg.encode())

    def check_extant(self):
        if len(self.server.extant) == 0 and not self.server.iterator:
            self.server.end = datetime.datetime.now()
            delta = self.server.end - self.server.start
            print 'Total Processing Time: %s' % delta
            print 'Total Messages Processed: %d' % self.server.count

    def do_debug(self, cmd):
        args = {'extant' : self.server.extant,
                'count' : self.server.count}
        msg = LQSMessage('debug', args)
        self.respond(msg)

    def do_next(self, cmd):
        out_msg = self.build_msg()
        if not out_msg.is_empty():
            self.server.count += 1
            self.server.extant.append(out_msg['id'])
        self.respond(out_msg)

    def do_delete(self, cmd):
        if len(cmd.args) != 1:
            self.error(cmd, 'delete command requires message id')
        else:
            mid = cmd.args[0]
            try:
                self.server.extant.remove(mid)
            except ValueError:
                self.error(cmd, 'message id not found')
            args = {'deleted' : True}
            msg = LQSMessage(mid, args)
            self.respond(msg)
            self.check_extant()

    def error(self, cmd, error_msg=None):
        args = {'error_msg' : error_msg,
                'cmd_name' : cmd.name,
                'cmd_args' : cmd.args}
        msg = LQSMessage('error', args)
        self.respond(msg)

    def do_stop(self, cmd):
        sys.exit(0)

    def handle(self):
        cmd = self.get_cmd()
        if hasattr(self, 'do_%s' % cmd.name):
            method = getattr(self, 'do_%s' % cmd.name)
            method(cmd)
        else:
            self.error(cmd, 'unrecognized command')

class PersistHandler(LQSHandler):

    def build_msg(self):
        if not self.server.iterator:
            return LQSMessage(None)
        try:
            obj = self.server.iterator.next()
            msg = LQSMessage(obj.id, self.server.args)
            return msg
        except StopIteration:
            self.server.iterator = None
            return LQSMessage(None)

def test_file(path, args=None):
    l = os.listdir(path)
    if not args:
        args = {}
    args['path'] = path
    s = LQSServer('', LQSHandler, iter(l), args)
    print "Awaiting UDP messages on port %d" % s.PORT
    s.serve_forever()

def test_simple(n):
    l = range(0, n)
    s = LQSServer('', LQSHandler, iter(l), None)
    print "Awaiting UDP messages on port %d" % s.PORT
    s.serve_forever()

