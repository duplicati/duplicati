import os, sys, time, traceback
import smtplib
from boto.utils import ShellCommand, get_ts
import boto

class ScriptBase:

    def __init__(self, config_file=None):
        self.instance_id = boto.config.get('Instance', 'instance-id', 'default')
        self.name = self.__class__.__name__
        self.ts = get_ts()
        if config_file:
            boto.config.read(config_file)

    def notify(self, subject, body=''):
        to_string = boto.config.get_value('Notification', 'smtp_to', None)
        if to_string:
            try:
                from_string = boto.config.get_value('Notification', 'smtp_from', 'boto')
                msg = "From: %s\n" % from_string
                msg += "To: %s\n" % to_string
                msg += "Subject: %s\n\n" % subject
                msg += body
                smtp_host = boto.config.get_value('Notification', 'smtp_host', 'localhost')
                server = smtplib.SMTP(smtp_host)
                smtp_user = boto.config.get_value('Notification', 'smtp_user', '')
                smtp_pass = boto.config.get_value('Notification', 'smtp_pass', '')
                if smtp_user:
                    server.login(smtp_user, smtp_pass)
                server.sendmail(from_string, to_string, msg)
                server.quit()
            except:
                boto.log.error('notify failed')

    def mkdir(self, path):
        if not os.path.isdir(path):
            try:
                os.mkdir(path)
            except:
                boto.log.error('Error creating directory: %s' % path)

    def umount(self, path):
        if os.path.ismount(path):
            self.run('umount %s' % path)

    def run(self, command, notify=True, exit_on_error=False):
        self.last_command = ShellCommand(command)
        if self.last_command.status != 0:
            boto.log.error(self.last_command.output)
            if notify:
                self.notify('Error encountered', self.last_command.output)
            if exit_on_error:
                sys.exit(-1)
        return self.last_command.status

    def main(self):
        pass
        
