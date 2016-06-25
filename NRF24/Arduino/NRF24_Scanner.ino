#include <SPI.h>
#include "nRF24L01.h"
#include "RF24.h"
#include "printf.h"

#define CE_PIN  9  /// Change it for your board
#define CSN_PIN 8  /// Change it for your board
#define csn(a) digitalWrite(CSN_PIN, a)
#define ce(a) digitalWrite(CE_PIN, a)

RF24 radio(CE_PIN, CSN_PIN); 

const char tohex[] = {
  '0','1','2','3','4','5','6','7','8','9','A','B','C','D','E','F'};
uint64_t pipe = 0xAALL;
//uint64_t pipe = 0x55LL;

byte buff[33];
byte chan=0;
byte len = 32;
byte addr_len = 2;
rf24_datarate_e data_rate = RF24_2MBPS;
byte go = 0;
uint8_t n(uint8_t reg, uint8_t value)                                       
{
  uint8_t status;

  csn(LOW);
  status = SPI.transfer( W_REGISTER | ( REGISTER_MASK & reg ) );
  SPI.transfer(value);
  csn(HIGH);
  return status;
}

void set_nrf(){
  radio.setAutoAck(false);
  radio.setPALevel(RF24_PA_MIN); //radio.setPALevel(RF24_PA_MAX); 
  radio.setDataRate(data_rate);
  //radio.setCRCLength(RF24_CRC_DISABLED);
  //radio.setAddressWidth(addr_len);
  radio.setPayloadSize(len);
  //radio.enableDynamicPayloads();
  radio.setChannel(chan);
  // RF24 doesn't ever fully set this -- only certain bits of it
  n(0x02, 0x00); 
  // RF24 doesn't have a native way to change MAC...
  // 0x00 is "invalid" according to the datasheet, but Travis Goodspeed found it works :)
  n(0x03, 0x00);

  radio.openReadingPipe(0, pipe); //radio.openReadingPipe(1, pipe);
  radio.disableCRC();
  radio.startListening();  
  //radio.printDetails();
}

String ReadLine(){
  String inData = "";
  while (Serial.available() > 0)
  {
    char recieved = Serial.read();

    if (recieved == '\n')
    {
      return inData;
    }
    else if(recieved != '\r')
    {
      inData += recieved; 
    }
  }

  return "";
}

void setup() {
  Serial.begin(2000000);
  printf_begin();
  radio.begin();
  set_nrf();
  Serial.print(0xFF);
}

long t1 = 0;
long t2 = 0;
unsigned long tr = 0;

void loop() {
  String in;
  in = ReadLine();

  if(in =="!"){
    go = 1;
    Serial.print("!\n"); 
  }
  if(in.charAt(0) == 'c'){
    chan = in.substring(1).toInt();
    radio.setChannel(chan);
    Serial.print("\n"); 
    Serial.print(chan);
    Serial.print("\n");
  }
  if (in == "+" || in == "w") {
    chan+=1;
    if(chan > 128) chan = 0;
    radio.setChannel(chan);
    Serial.print("\n"); 
    Serial.print(chan);
    Serial.print("\n");
  }
  if (in == "-" || in == "s") {
    chan-=1;
    if(chan > 128) chan = 128;
    radio.setChannel(chan);
    Serial.print("\n"); 
    Serial.print(chan);
    Serial.print("\n");
  }
  if (in == "q") {
    Serial.print("\n"); 
    radio.printDetails();
    Serial.print("\n");
  }  
  if (in == "a") {
    pipe = 0xAALL;
    set_nrf();
    Serial.print("\n0xAA\n");
  }
  if (in == "5") {
    pipe = 0x55LL;
    set_nrf();
    Serial.print("\n0x55\n");
  }
  if (in == "d") {
    switch(data_rate)
    {
    case RF24_250KBPS:
      data_rate = RF24_1MBPS;
      break;
    case RF24_1MBPS:
      data_rate = RF24_2MBPS;
      break;
    case RF24_2MBPS:
      data_rate = RF24_250KBPS;
      break;       
    default:
      data_rate = RF24_250KBPS;
      break;
    }
    set_nrf();
    Serial.print("\n");
    Serial.print(radio.getDataRateStr());
    Serial.print("\n");
  }

  while (go && radio.available()) {                      
    t2 = t1;
    t1 = micros();
    tr+=1;
    radio.read(&buff, sizeof(buff)); 
    Serial.print("\ndata,");
    Serial.print(tr);
    Serial.print(","); 
    Serial.print(millis());
    Serial.print(",");
    Serial.print(chan);
    Serial.print(",");
    Serial.print(radio.getDataRateStr());
    Serial.print(",0x");
    for (byte i=0; i < 5; i++){
      Serial.print(tohex[(byte)buff[i]>>4]);
      Serial.print(tohex[(byte)buff[i]&0x0f]);      
    }
    Serial.print(",0x");
    for (byte i=5; i < sizeof(buff); i++){
      Serial.print(tohex[(byte)buff[i]>>4]);
      Serial.print(tohex[(byte)buff[i]&0x0f]);      
    }
    Serial.print(",atad\n");
  }
}

