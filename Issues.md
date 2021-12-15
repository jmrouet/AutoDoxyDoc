Dear author @jonimat.

I don't find a way to send you a message, so I'm trying to create a issues.md file

I was using AutoDoxyDoc extension in visual studio and came accross an issue with an infinite loop occuring in 

AutoDoxyDoc\DoxygenCompletionCommandHandler.cs around line 247 (method NewCommandLine())

            while (!currentLine.StartsWith("/*!"))
            {
                if (m_regexTagSection.IsMatch(currentLine))
                {
                    extraIndent = m_configService.Config.TagIndentation;
                    break;
                }

                ts.LineUp();
                currentLine = ts.ActivePoint.CreateEditPoint().GetLines(ts.ActivePoint.Line, ts.ActivePoint.Line + 1);
                currentLine = currentLine.TrimStart();
            }
            
 
This occurs when I hit the return key at the end of this line (line 7: *major = 1;)

              /***************************************************************************
              ** Copyright (C) 2021 
              ***************************************************************************/

              void GetVersion(int * major, int * minor, int * patch) {
                  if (major != nullptr) {
                      *major = 1;
                  }
                  if (minor != nullptr) {
                      *minor = 0;
                  }
                  if (patch != nullptr) {
                      *patch = 0;
                  }
              }

as you can see there is no doxygen comment in this file, but the while() loop loops indefinitely. ts.LineUp() reaches the top of the document and nothing new happens.
I think AutoDoxyDoc is thinking the line is a comment but it is not!

Can you please have a look to it ?
