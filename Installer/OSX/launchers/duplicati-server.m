#import "run-with-mono.h"

NSString * const ASSEMBLY = @"Duplicati.Server.exe";
NSString * const APP_NAME = @"Duplicati.Server";
int const MONO_VERSION_MAJOR = 5;
int const MONO_VERSION_MINOR = 0;

int main() {
	@autoreleasepool {
		return [RunWithMono runAssemblyWithMono:APP_NAME assembly:ASSEMBLY major:MONO_VERSION_MAJOR minor:MONO_VERSION_MINOR];
	}
}

