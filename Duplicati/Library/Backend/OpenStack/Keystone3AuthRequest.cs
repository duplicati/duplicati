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

namespace Duplicati.Library.Backend.OpenStack;

internal class Keystone3AuthRequest
{
    public class AuthContainer
    {
        public Identity? identity { get; set; }
        public Scope? scope { get; set; }
    }

    public class Identity
    {
        public IdentityMethods[] methods { get; set; }

        public PasswordBasedRequest? password { get; set; }

        public Identity()
        {
            methods = new[] { IdentityMethods.password };
        }
    }

    public class Scope
    {
        public Project? project { get; set; }
    }

    public enum IdentityMethods
    {
        password,
    }

    public class PasswordBasedRequest
    {
        public UserCredentials? user { get; set; }
    }

    public class UserCredentials
    {
        public Domain domain { get; set; }
        public string name { get; set; }
        public string password { get; set; }

        public UserCredentials()
        {
            domain = null!;
            name = null!;
            password = null!;
        }
        public UserCredentials(Domain domain, string name, string password)
        {
            this.domain = domain;
            this.name = name;
            this.password = password;
        }

    }

    public class Domain
    {
        public string? name { get; set; }

        public Domain(string? name)
        {
            this.name = name;
        }
    }

    public class Project
    {
        public Domain domain { get; set; }
        public string name { get; set; }

        public Project(Domain domain, string name)
        {
            this.domain = domain;
            this.name = name;
        }
    }

    public AuthContainer auth { get; set; }

    public Keystone3AuthRequest(string? domain_name, string username, string? password, string? project_name)
    {
        Domain domain = new Domain(domain_name);

        auth = new AuthContainer();
        auth.identity = new Identity();
        auth.identity.password = new PasswordBasedRequest();
        auth.identity.password.user = new UserCredentials(domain, username, password!);
        auth.scope = new Scope();
        auth.scope.project = new Project(domain, project_name!);
    }
}