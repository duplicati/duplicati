# Copyright (c) 2006,2007 Mitch Garnaat http://garnaat.org/
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
import os, pwd
import boto
from boto.utils import get_instance_metadata, get_instance_userdata
from boto.pyami.config import Config, BotoConfigPath
from boto.pyami.scriptbase import ScriptBase

class Bootstrap(ScriptBase):
    """
    The Bootstrap class is instantiated and run as part of the PyAMI
    instance initialization process.  The methods in this class will
    be run from the rc.local script of the instance and will be run
    as the root user.

    The main purpose of this class is to make sure the boto distribution
    on the instance is the one required.
    """

    def __init__(self):
        self.working_dir = '/mnt/pyami'
        self.write_metadata()
        ScriptBase.__init__(self)

    def write_metadata(self):
        fp = open(os.path.expanduser(BotoConfigPath), 'w')
        fp.write('[Instance]\n')
        inst_data = get_instance_metadata()
        for key in inst_data:
            fp.write('%s = %s\n' % (key, inst_data[key]))
        user_data = get_instance_userdata()
        fp.write('\n%s\n' % user_data)
        fp.write('[Pyami]\n')
        fp.write('working_dir = %s\n' % self.working_dir)
        fp.close()
        # This file has the AWS credentials, should we lock it down?
        # os.chmod(BotoConfigPath, stat.S_IREAD | stat.S_IWRITE)
        # now that we have written the file, read it into a pyami Config object
        boto.config = Config()
        boto.init_logging()

    def create_working_dir(self):
        boto.log.info('Working directory: %s' % self.working_dir)
        if not os.path.exists(self.working_dir):
            os.mkdir(self.working_dir)

    def load_boto(self):
        update = boto.config.get('Boto', 'boto_update', 'svn:HEAD')
        if update.startswith('svn'):
            if update.find(':') >= 0:
                method, version = update.split(':')
                version = '-r%s' % version
            else:
                version = '-rHEAD'
            location = boto.config.get('Boto', 'boto_location', '/usr/local/boto')
            self.run('svn update %s %s' % (version, location))
        else:
            # first remove the symlink needed when running from subversion
            self.run('rm /usr/local/lib/python2.5/site-packages/boto')
            self.run('easy_install %s' % update)

    def fetch_s3_file(self, s3_file):
        try:
            if s3_file.startswith('s3:'):
                bucket_name, key_name = s3_file[len('s3:'):].split('/')
                c = boto.connect_s3()
                bucket = c.get_bucket(bucket_name)
                key = bucket.get_key(key_name)
                boto.log.info('Fetching %s/%s' % (bucket.name, key.name))
                path = os.path.join(self.working_dir, key.name)
                key.get_contents_to_filename(path)
        except:
            boto.log.exception('Problem Retrieving file: %s' % s3_file)
            path = None
        return path

    def load_packages(self):
        package_str = boto.config.get('Pyami', 'packages')
        if package_str:
            packages = package_str.split(',')
            for package in packages:
                package = package.strip()
                if package.startswith('s3:'):
                    package = self.fetch_s3_file(package)
                if package:
                    # if the "package" is really a .py file, it doesn't have to
                    # be installed, just being in the working dir is enough
                    if not package.endswith('.py'):
                        self.run('easy_install -Z %s' % package, exit_on_error=False)

    def main(self):
        self.create_working_dir()
        self.load_boto()
        self.load_packages()
        self.notify('Bootstrap Completed for %s' % boto.config.get_instance('instance-id'))

if __name__ == "__main__":
    # because bootstrap starts before any logging configuration can be loaded from
    # the boto config files, we will manually enable logging to /var/log/boto.log
    boto.set_file_logger('bootstrap', '/var/log/boto.log')
    bs = Bootstrap()
    bs.main()
