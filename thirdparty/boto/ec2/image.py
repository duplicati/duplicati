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
Represents an EC2 Image
"""

class Image:
    
    def __init__(self, connection=None):
        self.connection = connection
        self.id = None
        self.location = None
        self.state = None
        self.ownerId = None
        self.is_public = False
        self.product_codes = []

    def __repr__(self):
        return 'Image:%s' % self.id

    def startElement(self, name, attrs, connection):
        return None

    def endElement(self, name, value, connection):
        if name == 'imageId':
            self.id = value
        elif name == 'imageLocation':
            self.location = value
        elif name == 'imageState':
            self.state = value
        elif name == 'imageOwnerId':
            self.ownerId = value
        elif name == 'isPublic':
            if value == 'false':
                self.is_public = False
            elif value == 'true':
                self.is_public = True
            else:
                raise Exception(
                    'Unexpected value of isPublic %s for image %s'%(
                        value, 
                        self.id
                    )
                )
        elif name == 'productCode':
            self.product_codes.append(value)
        else:
            setattr(self, name, value)

    def run(self, min_count=1, max_count=1, key_name=None,
            security_groups=None, user_data=None,
            addressing_type=None, instance_type='m1.small', placement=None):
        """
        Runs this instance.
        
        @type min_count: int
        @param min_count: The minimum number of instances to start
        
        @type max_count: int
        @param max_count: The maximum number of instances to start
        
        @type key_name: string
        @param key_name: The keypair to run this instance with.
        
        @type security_groups: 
        @param security_groups:
        
        @type user_data: 
        @param user_data:
        
        @type addressing_type: 
        @param daddressing_type:
        
        @type instance_type: string
        @param instance_type: The type of instance to run (m1.small, m1.large, m1.xlarge)
        
        @type placement: 
        @param placement: 
        """
        return self.connection.run_instances(self.id, min_count, max_count,
                                             key_name, security_groups,
                                             user_data, addressing_type,
                                             instance_type, placement)

    def deregister(self):
        return self.connection.deregister_image(self.id)

    def get_launch_permissions(self):
        img_attrs = self.connection.get_image_attribute(self.id,
                                                        'launchPermission')
        return img_attrs.attrs

    def set_launch_permissions(self, user_ids=None, group_names=None):
        return self.connection.modify_image_attribute(self.id,
                                                      'launchPermission',
                                                      'add',
                                                      user_ids,
                                                      group_names)

    def remove_launch_permissions(self, user_ids=None, group_names=None):
        return self.connection.modify_image_attribute(self.id,
                                                      'launchPermission',
                                                      'remove',
                                                      user_ids,
                                                      group_names)

    def reset_launch_attributes(self):
        return self.connection.reset_image_attribute(self.id,
                                                     'launchPermission')

    def get_kernel(self):
        img_attrs =self.connection.get_image_attribute(self.id, 'kernel')
        return img_attrs.kernel

    def get_ramdisk(self):
        img_attrs = self.connection.get_image_attribute(self.id, 'ramdisk')
        return img_attrs.ramdisk

class ImageAttribute:

    def __init__(self, parent=None):
        self.name = None
        self.kernel = None
        self.ramdisk = None
        self.attrs = {}

    def startElement(self, name, attrs, connection):
        return None

    def endElement(self, name, value, connection):
        if name == 'launchPermission':
            self.name = 'launch_permission'
        elif name == 'productCodes':
            self.name = 'product_codes'
            self.attrs[self.name] = []
        elif name == 'group':
            if self.attrs.has_key('groups'):
                self.attrs['groups'].append(value)
            else:
                self.attrs['groups'] = [value]
        elif name == 'userId':
            if self.attrs.has_key('user_ids'):
                self.attrs['user_ids'].append(value)
            else:
                self.attrs['user_ids'] = [value]
        elif name == 'imageId':
            self.image_id = value
        elif name == 'productCode':
            self.attrs['product_codes'].append(value)
        elif name == 'kernel':
            self.kernel = value
        elif name == 'ramdisk':
            self.ramdisk = value
        else:
            setattr(self, name, value)
