#!/usr/bin/env python

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

"""
Some unit tests for the EC2Connection
"""

import unittest
import time
import os
from boto.ec2.connection import EC2Connection
import telnetlib
import socket

class EC2ConnectionTest (unittest.TestCase):

    def test_1_basic(self):
        # this is my user_id, if you want to run these tests you should
        # replace this with yours or they won't work
        user_id = '084307701560'
        print '--- running EC2Connection tests ---'
        c = EC2Connection()
        # get list of private AMI's
        rs = c.get_all_images(owners=[user_id])
        assert len(rs) > 0
        # now pick the first one
        image = rs[0]
        # temporarily make this image runnable by everyone
        status = image.set_launch_permissions(group_names=['all'])
        assert status
        d = image.get_launch_permissions()
        assert d.has_key('groups')
        assert len(d['groups']) > 0
        # now remove that permission
        status = image.remove_launch_permissions(group_names=['all'])
        assert status
        d = image.get_launch_permissions()
        assert not d.has_key('groups')
        
        # create a new security group
        group_name = 'test-%d' % int(time.time())
        group_desc = 'This is a security group created during unit testing'
        group = c.create_security_group(group_name, group_desc)
        # now get a listing of all security groups and look for our new one
        rs = c.get_all_security_groups()
        found = False
        for g in rs:
            if g.name == group_name:
                found = True
        assert found
        # now pass arg to filter results to only our new group
        rs = c.get_all_security_groups([group_name])
        assert len(rs) == 1
        group = rs[0]
        #
        # now delete the security group
        status = c.delete_security_group(group_name)
        # now make sure it's really gone
        rs = c.get_all_security_groups()
        found = False
        for g in rs:
            if g.name == group_name:
                found = True
        assert not found
        # now create it again for use with the instance test
        group = c.create_security_group(group_name, group_desc)
        
        # now try to launch apache image with our new security group
        rs = c.get_all_images()
        img_loc = 'ec2-public-images/fedora-core4-apache.manifest.xml'
        for image in rs:
            if image.location == img_loc:
                break
        reservation = image.run(security_groups=[group.name])
        instance = reservation.instances[0]
        while instance.state != 'running':
            print '\tinstance is %s' % instance.state
            time.sleep(30)
            instance.update()
        # instance in now running, try to telnet to port 80
        t = telnetlib.Telnet()
        try:
            t.open(instance.dns_name, 80)
        except socket.error:
            pass
        # now open up port 80 and try again, it should work
        group.authorize('tcp', 80, 80, '0.0.0.0/0')
        t.open(instance.dns_name, 80)
        t.close()
        # now revoke authorization and try again
        group.revoke('tcp', 80, 80, '0.0.0.0/0')
        try:
            t.open(instance.dns_name, 80)
        except socket.error:
            pass
        # now kill the instance and delete the security group
        instance.stop()
        # unfortunately, I can't delete the sg within this script
        #sg.delete()
        
        # create a new key pair
        key_name = 'test-%d' % int(time.time())
        status = c.create_key_pair(key_name)
        assert status
        # now get a listing of all key pairs and look for our new one
        rs = c.get_all_key_pairs()
        found = False
        for k in rs:
            if k.name == key_name:
                found = True
        assert found
        # now pass arg to filter results to only our new key pair
        rs = c.get_all_key_pairs([key_name])
        assert len(rs) == 1
        key_pair = rs[0]
        # now delete the key pair
        status = c.delete_key_pair(key_name)
        # now make sure it's really gone
        rs = c.get_all_key_pairs()
        found = False
        for k in rs:
            if k.name == key_name:
                found = True
        assert not found

        # short test around Paid AMI capability
        demo_paid_ami_id = 'ami-bd9d78d4'
        demo_paid_ami_product_code = 'A79EC0DB'
        l = c.get_all_images([demo_paid_ami_id])
        assert len(l) == 1
        assert len(l[0].product_codes) == 1
        assert l[0].product_codes[0] == demo_paid_ami_product_code
        
        print '--- tests completed ---'
