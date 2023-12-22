namespace Duplicati.Library.Backend.AliyunOSS
{
    /// <summary>
    /// 阿里云 OSS 配置
    /// https://help.aliyun.com/document_detail/31947.html
    /// </summary>
    public class AliyunOSSOptions
    {
        /// <summary>
        /// 存储空间是您用于存储对象（Object）的容器，所有的对象都必须隶属于某个存储空间。
        /// </summary>
        public string BucketName { get; set; }

        /// <summary>
        /// 地域表示 OSS 的数据中心所在物理位置。
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// AccessKeyId用于标识用户，AccessKeySecret是用户用于加密签名字符串和OSS用来验证签名字符串的密钥，其中AccessKeySecret 必须保密。
        /// </summary>
        public string AccessKeyId { get; set; }

        /// <summary>
        /// AccessKeyId用于标识用户，AccessKeySecret是用户用于加密签名字符串和OSS用来验证签名字符串的密钥，其中AccessKeySecret 必须保密。
        /// </summary>
        public string AccessKeySecret { get; set; }

        /// <summary>
        /// Endpoint 表示OSS对外服务的访问域名。
        /// https://help.aliyun.com/zh/oss/user-guide/regions-and-endpoints#concept-zt4-cvy-5db
        /// eg.
        /// oss-cn-guangzhou.aliyuncs.com
        /// oss-cn-hongkong.aliyuncs.com
        /// oss-us-west-1.aliyuncs.com
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// A path or subfolder in a bucket
        /// </summary>
        public string Path { get; set; }
    }
}
