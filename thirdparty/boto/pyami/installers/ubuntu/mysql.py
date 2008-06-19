# Copyright (c) 2006,2007,2008 Mitch Garnaat http://garnaat.org/
#
# Permission is hereby granted, free of charge, to any person obtaining a
# copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including
# without limitation the rights to use, copy, modify, merge, publish, dis-
# tribute, sublicense, and/or sell copies of the Software, and to permit
# persons to whom the Software is furnished to do so, subject to the fol-
# lowing conditions:
#
# The above copyright notice and this permission notice shall be included
# in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
# OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABIL-
# ITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
# SHALL THE AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.
#
from boto.pyami.installers.ubuntu.installer import Installer
import os
import boto
from ConfigParser import SafeConfigParser
import subprocess
import time

class MySQL(Installer):

    def install(self):
        self.run('apt-get update')
        self.run('apt-get -y install mysql-server', notify=True, exit_on_error=True)

    def set_root_password(self, password=None):
        if not password:
            password = boto.config.get('Pyami', 'mysql_root_password')
        if password:
            self.run('mysqladmin -u root password %s' % password)

    def change_data_dir(self):
        fresh_install = False;
        time.sleep(2) #trying to stop mysql immediately after installing it fails
        self.stop('mysql')
        if not os.path.exists('/mnt/mysql'):
            self.run('mkdir /mnt/mysql')
            fresh_install = True;
        self.run('chown -R mysql:mysql /mnt/mysql')
        fp = open('/etc/mysql/conf.d/use_mnt.cnf', 'w')
        fp.write('# created by pyami\n')
        fp.write('# use the /mnt volume for data\n')
        fp.write('[mysqld]\n')
        fp.write('datadir = /mnt/mysql\n')
        fp.write('log_bin = /mnt/mysql/mysql-bin.log\n')
        fp.close()
        if fresh_install:
            self.run('cp -pr /var/lib/mysql/* /mnt/mysql/')
            self.run('cp -pr /var/log/mysql/* /mnt/mysql/')
            self.start('mysql')
        else:
            #get the password ubuntu expects to use:
            config_parser = SafeConfigParser()
            config_parser.read('/etc/mysql/debian.cnf')
            password = config_parser.get('client', 'password')
            # start the mysql deamon, then mysql with the required grant statement piped into it:
            self.start('mysql')
            time.sleep(1) #time for mysql to start
            grant_command = "echo \"GRANT ALL PRIVILEGES ON *.* TO 'debian-sys-maint'@'localhost' IDENTIFIED BY '%s' WITH GRANT OPTION;\" | mysql" % password
            self.run(grant_command)
            # leave mysqld running

    def main(self):
        self.install()
        self.set_root_password()
        self.change_data_dir()
        
