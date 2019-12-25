#import "run-with-mono.h"

@import Foundation;
@import AppKit;

NSString * const VERSION_TITLE = @"Cannot launch %@";
NSString * const VERSION_MSG = @"%@ requires the Mono Framework version %d.%d or later.";
NSString * const DOWNLOAD_URL = @"http://www.mono-project.com/download/stable/#download-mac";

// Helper method to see if the user has requested debug output
bool D() {
	NSString* v = [[[NSProcessInfo processInfo]environment]objectForKey:@"DEBUG"];
	if (v == nil || v.length == 0 || [v isEqual:@"0"] || [v isEqual:@"false"] || [v isEqual:@"f"])
		return false;
	return true;
}

// Wrapper method to invoke commandline operations and return the string output
NSString *runCommand(NSString *program, NSArray<NSString *> *arguments) {
	NSPipe *pipe = [NSPipe pipe];
	NSFileHandle *file = pipe.fileHandleForReading;

	NSTask *task = [[NSTask alloc] init];
	task.launchPath = program;
	task.arguments = arguments;
	task.standardOutput = pipe;

	[task launch];

	NSData *data = [file readDataToEndOfFile];
	[file closeFile];
	[task waitUntilExit];

	NSString *cmdOutput = [[NSString alloc] initWithData: data encoding: NSUTF8StringEncoding];
	if (cmdOutput == nil || cmdOutput.length == 0)
		return nil;

	return [cmdOutput stringByTrimmingCharactersInSet:
                              [NSCharacterSet whitespaceAndNewlineCharacterSet]];
}

// Checks if the Mono version is greater than or equal to the desired version
bool isValidMono(NSString *mono, int major, int minor) {
	NSFileManager *fileManager = [NSFileManager defaultManager];

	if (mono == nil)
		return false;

	if (![fileManager fileExistsAtPath:mono] || ![fileManager isExecutableFileAtPath:mono])
		return false;

	NSString *versionInfo = runCommand(mono, @[@"--version"]);

	NSRange rg = [versionInfo rangeOfString:@"Mono JIT compiler version \\d+\\.\\d+" options:NSRegularExpressionSearch];
	if (rg.location != NSNotFound) {
		versionInfo = [versionInfo substringWithRange:rg];
		if (D()) NSLog(@"Matched version: %@", versionInfo);
		rg = [versionInfo rangeOfString:@"\\d+\\.\\d+" options:NSRegularExpressionSearch];
		if (rg.location != NSNotFound) {
			versionInfo = [versionInfo substringWithRange:rg];
			if (D()) NSLog(@"Matched version: %@", versionInfo);

			NSArray<NSString *> *versionComponents = [versionInfo componentsSeparatedByString:@"."];
			if ([versionComponents[0] intValue] < major)
				return false;
			if ([versionComponents[1] intValue] < minor)
				return false;

			return true;
		}
	}

	return false;
}

// Attempts to locate a mono with a valid version
NSString *findMono(int major, int minor) {
	NSFileManager *fileManager = [NSFileManager defaultManager];

	NSString *currentMono = runCommand(@"/usr/bin/which", @[@"mono"]);
	if (D()) NSLog(@"which mono: %@", currentMono);

	if (isValidMono(currentMono, major, minor)) {
		if (D()) NSLog(@"Found mono with: %@", currentMono);
		return currentMono;
	}

	NSArray *probepaths = @[@"/usr/local/bin/mono", @"/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono", @"/opt/local/bin/mono"];
	for(NSString* probepath in probepaths) {
		if (D()) NSLog(@"Trying mono with: %@", probepath);
		if (isValidMono(probepath, major, minor)) {
			if (D()) NSLog(@"Found mono with: %@", probepath);
			return probepath;
		}
	}

	if (D()) NSLog(@"Failed to find Mono, returning: %@", nil);
	return nil;
}

// Shows the download dialog, prompting to download Mono
void showDownloadMonoDialog(NSString *appName, int major, int minor) {
	NSAlert *alert = [[NSAlert alloc] init];
	[alert setInformativeText:[NSString stringWithFormat:VERSION_MSG, appName, major, minor]];
	[alert setMessageText:[NSString stringWithFormat:VERSION_TITLE, appName]];
	[alert addButtonWithTitle:@"Cancel"];
	[alert addButtonWithTitle:@"Download"];
	NSModalResponse btn = [alert runModal];
	if (btn == NSAlertSecondButtonReturn) {
		if (D()) NSLog(@"Clicked download");
		runCommand(@"/usr/bin/open", @[DOWNLOAD_URL]);
		//[[UIApplication sharedApplication] openURL:[NSURL URLWithString:DOWNLOAD_URL] options:@{} completionHandler:nil];
	}
}

// Helper method to copy from source to target
void copyStream(NSFileHandle *source, NSFileHandle* target) {
 	NSData *data;
    do
    {
        data = [source availableData];
        //NSLog(@"Read some data %d", source.fileDescriptor);
        [target writeData: data];

    } while ([data length] > 0);
}

// Top-level method, finds Mono with an appropriate version and launches the assembly
int runAssemblyWithMono(NSString *appName, NSString *assembly, int major, int minor) {
	NSFileManager *fileManager = [NSFileManager defaultManager];

	NSString *entryFolder = [[NSBundle mainBundle] resourcePath];
	if (D()) NSLog(@"entryFolder: %@", entryFolder);

	NSString *assemblyPath = [NSString pathWithComponents:@[entryFolder, assembly]];
	if (D()) NSLog(@"assemblyPath: %@", assemblyPath);

	if (![fileManager fileExistsAtPath:assemblyPath]) {
		NSLog(@"Assembly file not found: %@", assemblyPath);
		return 1;
	}

	NSString *currentMono = findMono(major, minor);
	if (currentMono == nil) {
		NSLog(@"No valid mono found!");
		showDownloadMonoDialog(appName, major, minor);
		return 1;
	}

	if (D()) NSLog(@"Running %@ %@", currentMono, assemblyPath);

	// Copy commandline arguments
	NSMutableArray* arguments = [[NSMutableArray alloc] init];
	[arguments addObjectsFromArray:[[NSProcessInfo processInfo] arguments]];
	
	// replace the executable-path with the assembly path
	[arguments replaceObjectAtIndex:0 withObject:assemblyPath];

	NSTask *task = [[NSTask alloc] init];
	task.launchPath = currentMono;
	task.arguments = arguments;

	// Setup forwarding of stdout
	NSPipe *stdout_pipe = [NSPipe pipe];
	NSPipe *stderr_pipe = [NSPipe pipe];
	NSPipe *stdin_pipe = [NSPipe pipe];

    [task setStandardOutput:stdout_pipe];
    [task setStandardError:stderr_pipe];
    [task setStandardInput:stdin_pipe];

    NSFileHandle *stdout_source = [stdout_pipe fileHandleForReading];
    NSFileHandle *stderr_source = [stderr_pipe fileHandleForReading];
    NSFileHandle *stdin_target = [stdin_pipe fileHandleForWriting];

    NSFileHandle *stdout_target = [NSFileHandle fileHandleWithStandardOutput];
    NSFileHandle *stderr_target = [NSFileHandle fileHandleWithStandardError];
    NSFileHandle *stdin_source = [NSFileHandle fileHandleWithStandardInput];

	[task launch];

	if (D()) NSLog(@"Setting up stream forwards");
	dispatch_queue_t bgQueue1 = dispatch_queue_create("bgQueue1", NULL);
	dispatch_async(bgQueue1, ^{
		copyStream(stdin_source, stdin_target);
		[stdin_source closeFile];
		[stdin_target closeFile];
	});
	dispatch_queue_t bgQueue2 = dispatch_queue_create("bgQueue2", NULL);
	dispatch_async(bgQueue2, ^{
		copyStream(stdout_source,  stdout_target);
		[stdout_source closeFile];
		[stdout_target closeFile];
	});
	dispatch_queue_t bgQueue3 = dispatch_queue_create("bgQueue3", NULL);
	dispatch_async(bgQueue3, ^{
		copyStream(stderr_source, stderr_target);
		[stderr_source closeFile];
		[stderr_target closeFile];
	});

	if (D()) NSLog(@"Waiting for exit");
	[task waitUntilExit];

	if (D()) NSLog(@"Returning status code");
	return [task terminationStatus];
}

@implementation RunWithMono
+ (int) runAssemblyWithMono:(NSString *)appName assembly:(NSString *)assembly major:(int) major minor:(int) minor {
	return runAssemblyWithMono(appName, assembly, major, minor);
}
@end

