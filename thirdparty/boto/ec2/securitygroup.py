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
Represents an EC2 Security Group
"""

class SecurityGroup:
    
    def __init__(self, connection=None, owner_id=None,
                 name=None, description=None):
        self.connection = connection
        self.owner_id = owner_id
        self.name = name
        self.description = description
        self.rules = []

    def __repr__(self):
        return 'SecurityGroup:%s' % self.name

    def startElement(self, name, attrs, connection):
        if name == 'item':
            self.rules.append(IPPermissions(self))
            return self.rules[-1]
        else:
            return None

    def endElement(self, name, value, connection):
        if name == 'ownerId':
            self.owner_id = value
        elif name == 'groupName':
            self.name = value
        elif name == 'groupDescription':
            self.description = value
        elif name == 'ipRanges':
            pass
        elif name == 'return':
            if value == 'false':
                self.status = False
            elif value == 'true':
                self.status = True
            else:
                raise Exception(
                    'Unexpected value of status %s for image %s'%(
                        value, 
                        self.id
                    )
                )
        else:
            setattr(self, name, value)

    def delete(self):
        return self.connection.delete_security_group(self.name)

    def add_rule(self, ip_protocol, from_port, to_port,
                 src_group_name, src_group_owner_id, cidr_ip):
        rule = IPPermissions(self)
        rule.ip_protocol = ip_protocol
        rule.from_port = from_port
        rule.to_port = to_port
        self.rules.append(rule)
        rule.add_grant(src_group_name, src_group_owner_id, cidr_ip)

    def remove_rule(self, ip_protocol, from_port, to_port,
                    src_group_name, src_group_owner_id, cidr_ip):
        target_rule = None
        for rule in self.rules:
            if rule.ip_protocol == ip_protocol:
                if rule.from_port == from_port:
                    if rule.to_port == to_port:
                        target_rule = rule
                        target_grant = None
                        for grant in rule.grants:
                            if grant.group_name == src_group_name:
                                if grant.user_id == src_group_owner_id:
                                    if grant.cidr_ip == cidr_ip:
                                        target_grant = grant
                        if target_grant:
                            rule.grants.remove(target_grant)
        if len(rule.grants) == 0:
            self.rules.remove(target_rule)

    def authorize(self, ip_protocol=None, from_port=None, to_port=None,
                  cidr_ip=None, src_group=None):
        if src_group:
            src_group_name = src_group.name
            src_group_owner_id = src_group.owner_id
        else:
            src_group_name = None
            src_group_owner_id = None
        status = self.connection.authorize_security_group(self.name,
                                                          src_group_name,
                                                          src_group_owner_id,
                                                          ip_protocol,
                                                          from_port,
                                                          to_port,
                                                          cidr_ip)
        if status:
            self.add_rule(ip_protocol, from_port, to_port, src_group_name,
                          src_group_owner_id, cidr_ip)
        return status

    def revoke(self, ip_protocol=None, from_port=None, to_port=None,
               cidr_ip=None, src_group=None):
        if src_group:
            src_group_name = src_group.name
            src_group_owner_id = src_group.owner_id
        else:
            src_group_name = None
            src_group_owner_id = None
        status = self.connection.revoke_security_group(self.name,
                                                       src_group_name,
                                                       src_group_owner_id,
                                                       ip_protocol,
                                                       from_port,
                                                       to_port,
                                                       cidr_ip)
        if status:
            self.remove_rule(ip_protocol, from_port, to_port, src_group_name,
                             src_group_owner_id, cidr_ip)
        return status

class IPPermissions:

    def __init__(self, parent=None):
        self.parent = parent
        self.ip_protocol = None
        self.from_port = None
        self.to_port = None
        self.grants = []

    def __repr__(self):
        return 'IPPermissions:%s(%s-%s)' % (self.ip_protocol,
                                            self.from_port, self.to_port)

    def startElement(self, name, attrs, connection):
        if name == 'item':
            self.grants.append(GroupOrCIDR(self))
            return self.grants[-1]
        return None

    def endElement(self, name, value, connection):
        if name == 'ipProtocol':
            self.ip_protocol = value
        elif name == 'fromPort':
            self.from_port = value
        elif name == 'toPort':
            self.to_port = value
        else:
            setattr(self, name, value)

    def add_grant(self, user_id=None, group_name=None, cidr_ip=None):
        grant = GroupOrCIDR(self)
        grant.user_id = user_id
        grant.group_name = group_name
        grant.cidr_ip = cidr_ip
        self.grants.append(grant)
        return grant

class GroupOrCIDR:

    def __init__(self, parent=None):
        self.user_id = None
        self.group_name = None
        self.cidr_ip = None

    def __repr__(self):
        if self.cidr_ip:
            return '%s' % self.cidr_ip
        else:
            return '%s-%s' % (self.group_name, self.user_id)

    def startElement(self, name, attrs, connection):
        return None

    def endElement(self, name, value, connection):
        if name == 'userId':
            self.user_id = value
        elif name == 'groupName':
            self.group_name = value
        if name == 'cidrIp':
            self.cidr_ip = value
        else:
            setattr(self, name, value)

