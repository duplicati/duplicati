// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
namespace Duplicati.Library.Backend.AliyunOSS
{
    /// <summary>
    /// Aliyun OSS Configuration
    /// 阿里云 OSS 配置
    /// https://help.aliyun.com/document_detail/31947.html
    /// </summary>
    public class AliyunOSSOptions
    {
        /// <summary>
        /// A bucket is a container used for storing objects (Object), and all objects must belong to a bucket.
        /// 存储空间是您用于存储对象（Object）的容器，所有的对象都必须隶属于某个存储空间。
        /// </summary>
        public string BucketName { get; set; }

        /// <summary>
        /// AccessKeyId is used to identify the user. AccessKeySecret is the key used by the user to encrypt signature strings and by OSS to verify signature strings. The AccessKeySecret must be kept confidential.
        /// AccessKeyId用于标识用户，AccessKeySecret是用户用于加密签名字符串和OSS用来验证签名字符串的密钥，其中AccessKeySecret 必须保密。
        /// </summary>
        public string AccessKeyId { get; set; }

        /// <summary>
        /// AccessKeyId is used to identify the user. AccessKeySecret is the key used by the user to encrypt signature strings and by OSS to verify signature strings. The AccessKeySecret must be kept confidential.
        /// AccessKeyId用于标识用户，AccessKeySecret是用户用于加密签名字符串和OSS用来验证签名字符串的密钥，其中AccessKeySecret 必须保密。
        /// </summary>
        public string AccessKeySecret { get; set; }

        /// <summary>
        /// Endpoint represents the access domain name for OSS external services.
        /// Endpoint 表示OSS对外服务的访问域名。
        /// https://help.aliyun.com/zh/oss/user-guide/regions-and-endpoints#concept-zt4-cvy-5db
        /// e.g.
        /// oss-cn-guangzhou.aliyuncs.com
        /// oss-cn-hongkong.aliyuncs.com
        /// oss-us-west-1.aliyuncs.com
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// A path or subfolder in a bucket
        /// 桶中的路径或子文件夹
        /// </summary>
        public string Path { get; set; }
    }

}
