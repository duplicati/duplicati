import sys
import json
import rumps
import base64
from threading import Thread

from Foundation import (NSData)
from AppKit import NSImage

lookup = {}
app = None

def item_clicked(sender):
    for k in lookup:
        if lookup[k].title == sender.title:
            print "click:%s" % k
            sys.stdout.flush()


def parsemenus(cfg):
    menuitems = []
    for n in cfg["Menus"]:
        mn = rumps.MenuItem(n["Text"])
        if n.has_key("Enabled") and not n["Enabled"]:
            mn.set_callback(None)
        else:
            mn.set_callback(item_clicked)
        lookup[n["Key"]] = mn

        menuitems.append(mn)

    return menuitems

def get_input():
    try:        
        while True:
            line = sys.stdin.readline()
            if not line:
                break

            if line == '':
                continue

            print "info:Read %s" % line
            sys.stdout.flush()
            cfg = None
            try:
                cfg = json.loads(line)
            except:
                pass

            if cfg == None:
                print "info:Unable to parse line"
                sys.stdout.flush()
                continue

            if cfg.has_key("Action"):
                print "info:Running %s" % cfg["Action"]
                sys.stdout.flush()

                if cfg["Action"] == "setmenu":
                    menu = cfg["Menu"]

                    if lookup.has_key(menu["Key"]):
                        lookup[menu["Key"]].title = menu["Text"]

                        if menu.has_key("Enabled") and not menu["Enabled"]:
                            lookup[menu["Key"]].set_callback(None)
                        else:
                            lookup[menu["Key"]].set_callback(item_clicked)
                        app.menu.update([])
                    else:
                        print "warn:Key not found %s" % cfg["Action"]
                        sys.stdout.flush()



                elif cfg["Action"] == "setmenus":
                    app.menu.clear()
                    app.menu = parsemenus(cfg)
                    print "info:Updated menus"
                    sys.stdout.flush()
                elif cfg["Action"] == "seticon":

                    try:
                        raw = base64.b64decode(cfg["Image"])
                        data = NSData.dataWithBytes_length_(raw, len(raw))
                        img = NSImage.alloc().initWithData_(data)    
                        img.setScalesWhenResized_(True)
                        img.setSize_((21, 21))
                        app.icon = img

                        print "info:Image updated"
                        sys.stdout.flush()

                    except:
                        print "warn:Failed to set image"
                        sys.stdout.flush()

                elif cfg["Action"] == "shutdown":
                    break
                elif cfg["Action"] == "notification":
                    if rumps_NOTIFICATIONS:
                        rumps.notification(cfg["Title"], '', cfg["Message"])

    finally:
        rumps.quit_application()     
        print "info:Shutdown"
        sys.stdout.flush()

    print "info:Stdin close"
    sys.stdout.flush()
    sys.stdin.close()

if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "TEST":
        sys.exit(0)

    app = rumps.App('', quit_button=None)

    t = Thread(target=get_input)
    t.start()

    app.run()
