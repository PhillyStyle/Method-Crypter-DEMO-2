# Method Crypter DEMO 2
Encrypt Your Methods ***Inside Your EXEs!*** (In C# 4.8)

![Method Crypter 2 GUI](MethodCrypter2ScreenShot.png?raw=true)

Also do other fun stuff like: 
* Randomize Method names (and variable names)
* Inject junk code at the start of your encrypted methods
* Randomize your Assembly GUID
* Play Console Snake! (The encrypted payload in the Demo.)

## What is it?
Method Crypter Demo 2 is still a suite of 3 programs:
* Method Crypter.exe
* Crypted Demo.exe
* Server.exe

## What is New in 2.0?
* Smaller File size:
  * In version 2.0 the file size of Output.exe is dramatically reduced. From about 450 KB to just 23-26 KB.  This is done by not including dnlib in the Crypted.exe anymore.
* New payload injection method:
  * In version 2.0 we no longer use junk methods for our encryption overhead.  We don't encrypt to the method space anymore at all.  Instead we encrypt to base64 encoded strings and inject them into the file, then we replace the code in the methods we encrypted with stack neutral junk code.  Using a binary patching method that I created.
  * Output.exe no longer has to copy it's self to the temp directory to open it's self with dnlib because we don't use dnlib in Output.exe anymore.  We just use memory offsets now.
  * Now that there is real code in our methods, the JIT compiler gets ahead of us compiling code we don't want compiled yet, so I had to add the Force Re-JIT section to the dialog.
    * ForceReJit is the name of a method we use in Crypted Demo.exe, and all it does is force the Just In Time compiler to recompile code when we want it to.
    * ForceReJit would break if Randomize Method Names was checked, so I had to fix that.
    * So now in Method Crypter.exe, if you have Randomize Method Names Checked and Force Re-JIT contains a method name, it goes into your file, finds the Force Re-JIT method name string, and renames it to the new random method name string so that Force Re-JIT still works even when Randomize Method Names is checked. (I hope you can make some sense of that explanation.)
* No longer detected as a virus by Windows Defender!  At the time of me writing this (10/26/2025) this method is not detected as a virus and is safe to use in your programs for encryption/obfuscation, or whatever your reason is.

## What each program does:
* **Method Crypter.exe** is the program with the GUI I created (seen above) for encrypting the payload EXE (Crypted Demo.exe).
  * This is where you list the Types (namespace.class) and Methods that you are going to encrypt.
  * Set the AES Key and IV (I recommend using the Random AES Key & IV Button every time you encrypt.)
  * Pick the paths to the Crypted Demo.exe, Server.exe, and Output EXE.
  * If you are going to encrypt strings then you must define the Type and Array Name for your encrypted strings as defined in Crypted Demo.exe.
  * Choose options like Randomizing Method Names (recommended), Injecting junk code at the start of encrypted methods (To obfuscate method size), Randomizing the Assembly GUID (How often do we forget to do this as developers?), and Show the Method Inspector (A cool little Form that shows you insights about what methods are in your file, their names, and where, and how big, and stuff like that.)
  * ***Now it is time for the secret sauce.***  This has changed since Method Crypter Demo 1.  Now Method Crypter encrypts methods to base64 strings located in the Payload Strings Array.  It then replaces the code in the methods that you encrypted with stack neutral junk code. (leaving behind just a touch of the code that was there before for legitimacy but not enough to leave a signature.).  At runtime, our crypted output exe (Output.exe) connects to the Server.exe, and Server.exe tells Output.exe how to decrypt the encrypted methods.  Output.exe then runs the payload (Console Snake in this case).
 
* **Crypted Demo.exe** is the program with the payload. (The part that gets encrypted.)  In this case the payload is a Console Snake game, (It's pretty fun if I must say so myself.) but the payload can be whatever you want it to be.  Use your imagination.  How does crypted demo do it?
  * In this case, when you type "run" into the console, crypted demo connects to the Server.exe.  Server.exe then sends The AES Key/IV and tells Crypted Demo what methods to decrypt.  I will get into "Why use Server.exe at all" later.  Crypted demo then finds the methods in memory (based off offsets that Server.exe gives it), and decrypts the payload strings to them at runtime.  Then it executes the payload (The Snake Game).

* **Server.exe**.  Server.exe's only mission in life is to wait for a connection, and when a connection comes in, send the AES Key, and IV, and tell it what methods to decrypt via offsets. (Also what strings to decrypt if you encrypted any strings.)  The Server.exe traffic is not encrypted in the demo, but in real life application I recommend encrypting it.  I simply employed a simple text based protocol for communication since it is just a demo.  It is a pretty flexible little text based protocol though.  Feel free to use it in your own projects.
  * Now "Why use a Server.exe at all?"  This is a very important thing to understand.  Using a Server.exe ensures that the payload is secure from any decryption before it is needed.  It can't be decrypted in a virtual environment, or sandbox or anything, because even it doesn't know the Key/IV making this a pretty secure way of doing things.

## Some Notes:
1) This is just a Demo showing how things ***can*** be done.  Not necessarily how they ***have to*** be done.
2) The Crypted Demo.exe using this method cannot be used in any crypters or programs that do process injection, because it has to be able to find the methods in memory, and if you use it in process injection, it won't be able to find the methods and will probably crash.
3) If you run Crypted Demo.exe without encrypting it first, it will crash when you type "run".

## Building
Method Crypter Nuget Packages:
* dnlib 4.5.0
* System.Buffers 4.6.1
* System.Memory 4.6.3
* System.Numerics.Vectors 4.6.1
* System.Runtime.CompilerServices.Unsafe 6.1.2

Server Nuget Packages:
* System.Buffers 4.6.1
* System.Memory 4.6.3
* System.Numerics.Vectors 4.6.1
* System.Runtime.CompilerServices.Unsafe 6.1.2

Crypted Demo must be built with Allow unsafe code checked, and Optimize code unchecked.

## Thanks and Shoutout
Thanks to ChatGPT for helping me code this.

Shoutout to everyone on Hack Forums where I plan on publishing this.
